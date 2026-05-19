using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using DiscordBot.Core.Data;
using DiscordBot.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Services;

/// <summary>
/// Background service that polls Hudu release feed JSON and posts new releases to a Discord channel.
/// Last posted release ID is stored in SQLite via FeedPostState.
/// </summary>
public class HuduReleaseMonitorService : BackgroundService
{
    private const string FeedType = "HuduRelease";

    private readonly DiscordSocketClient _client;
    private readonly BotConfig _config;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<HuduReleaseMonitorService> _logger;
    private readonly HttpClient _httpClient;

    public HuduReleaseMonitorService(
        DiscordSocketClient client,
        BotConfig config,
        IServiceProvider serviceProvider,
        ILogger<HuduReleaseMonitorService> logger)
    {
        _client = client;
        _config = config;
        _serviceProvider = serviceProvider;
        _logger = logger;

        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("HuduCommunityBot/1.0");
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var monitorConfig = _config.HuduReleaseMonitor;
        if (!monitorConfig.Enabled || monitorConfig.ChannelId == 0)
        {
            _logger.LogInformation("Hudu release monitor is disabled or has no channel configured - skipping.");
            return;
        }

        while (_client.ConnectionState != ConnectionState.Connected && !stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }

        _logger.LogInformation(
            "Hudu release monitor started. Polling {FeedUrl} every {Interval} minute(s).",
            monitorConfig.FeedUrl,
            monitorConfig.PollIntervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Polling Hudu release feed now...");
                await PollFeedAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogWarning(ex, "Hudu release feed poll was canceled before completion; monitor will retry on next interval.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error polling Hudu release feed.");
            }

            await Task.Delay(TimeSpan.FromMinutes(monitorConfig.PollIntervalMinutes), stoppingToken);
        }
    }

    private async Task PollFeedAsync(CancellationToken cancellationToken)
    {
        await using var stream = await _httpClient.GetStreamAsync(_config.HuduReleaseMonitor.FeedUrl, cancellationToken);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var releases = await JsonSerializer.DeserializeAsync<List<HuduReleaseItem>>(stream, options, cancellationToken)
            ?? new List<HuduReleaseItem>();

        _logger.LogInformation("Fetched {ReleaseCount} items from Hudu release feed.", releases.Count);

        // Debug: log top 3 releases
        var topReleases = releases.Take(3).ToList();
        foreach (var r in topReleases)
        {
            _logger.LogDebug("Release: id={Id}, name={Name}, platform={Platform}, release_type={ReleaseType}, draft={Draft}",
                r.Id, r.Name, r.Platform, r.ReleaseType, r.Draft);
        }

        var relevantReleases = releases
            .Where(r => r.Id > 0)
            .Where(r => !r.Draft)
            .Where(r => string.Equals(r.ReleaseType, "stable", StringComparison.OrdinalIgnoreCase))
            .Where(r => string.Equals(r.Platform, "web", StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => r.Id)
            .ToList();

        _logger.LogInformation("Found {RelevantCount} stable web releases after filtering.", relevantReleases.Count);

        if (relevantReleases.Count == 0)
        {
            _logger.LogDebug("Hudu release feed returned no stable web releases.");
            return;
        }

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HuduCommunityBotContext>();
        var sourceId = _config.HuduReleaseMonitor.FeedUrl.Trim();
        var state = await db.FeedPostStates
            .FirstOrDefaultAsync(x => x.FeedType == FeedType && x.SourceId == sourceId, cancellationToken);

        if (state == null)
        {
            state = new FeedPostState
            {
                FeedType = FeedType,
                SourceId = sourceId,
                LastPostedItemId = _config.HuduReleaseMonitor.BaselineReleaseId.ToString(),
                LastCheckedAt = DateTime.UtcNow
            };

            db.FeedPostStates.Add(state);
            await db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Hudu release monitor initialized with baseline release ID {BaselineReleaseId}.",
                _config.HuduReleaseMonitor.BaselineReleaseId);

            return;
        }

        if (!int.TryParse(state.LastPostedItemId, out var lastPostedReleaseId) || lastPostedReleaseId <= 0)
        {
            lastPostedReleaseId = _config.HuduReleaseMonitor.BaselineReleaseId;
            state.LastPostedItemId = lastPostedReleaseId.ToString();
            state.LastCheckedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Hudu release monitor repaired invalid state and set baseline release ID to {BaselineReleaseId}.",
                lastPostedReleaseId);
            return;
        }

        var newReleases = relevantReleases
            .Where(r => r.Id > lastPostedReleaseId)
            .OrderBy(r => r.Id)
            .ToList();

        if (newReleases.Count == 0)
        {
            state.LastCheckedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        foreach (var release in newReleases)
        {
            await PostReleaseAsync(release);

            state.LastPostedItemId = release.Id.ToString();
            state.LastCheckedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task PostReleaseAsync(HuduReleaseItem release)
    {
        if (await _client.GetChannelAsync(_config.HuduReleaseMonitor.ChannelId) is not IMessageChannel channel)
        {
            _logger.LogWarning(
                "Hudu release monitor channel {ChannelId} was not found or is not a text channel.",
                _config.HuduReleaseMonitor.ChannelId);
            return;
        }

        var releaseUrl = string.IsNullOrWhiteSpace(release.Url)
            ? $"https://hq.hudu.com/releases/{release.Id}.json"
            : release.Url.Trim();

        var description = BuildDescription(release);

        var embed = new EmbedBuilder()
            .WithTitle($"Hudu Release {release.Name}")
            .WithColor(Color.Blue)
            .WithUrl(releaseUrl)
            .WithDescription(description)
            .WithFooter("Hudu Releases")
            .WithTimestamp(ResolveTimestamp(release));

        string? mentionText = null;
        if (_config.HuduReleaseMonitor.RoleId != 0)
        {
            mentionText = $"<@&{_config.HuduReleaseMonitor.RoleId}>";
        }

        await channel.SendMessageAsync(text: mentionText, embed: embed.Build());
        _logger.LogInformation("Posted Hudu release update for release ID {ReleaseId} ({Version}).", release.Id, release.Name);
    }

    private static string BuildDescription(HuduReleaseItem release)
    {
        var sourceText = !string.IsNullOrWhiteSpace(release.Headline)
            ? release.Headline
            : release.Notes;

        var text = HtmlToPlainText(sourceText);
        if (string.IsNullOrWhiteSpace(text))
        {
            text = "A new Hudu release is available.";
        }

        if (text.Length > 900)
        {
            text = text[..900].TrimEnd() + "...";
        }

        return $"**Version:** `{release.Name}`\n**Release ID:** `{release.Id}`\n\n{text}";
    }

    private static DateTimeOffset ResolveTimestamp(HuduReleaseItem release)
    {
        if (release.CreatedAt.HasValue)
        {
            return release.CreatedAt.Value;
        }

        if (!string.IsNullOrWhiteSpace(release.PublishedDate) && DateTimeOffset.TryParse(release.PublishedDate, out var parsedDate))
        {
            return parsedDate;
        }

        return DateTimeOffset.UtcNow;
    }

    private static string HtmlToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var withLineBreaks = Regex.Replace(html, "<(br|/div|/p|/li|/ul|/ol|/h1|/h2|/h3|/h4|/h5|/h6)[^>]*>", "\n", RegexOptions.IgnoreCase);
        var withoutTags = Regex.Replace(withLineBreaks, "<[^>]+>", " ");
        var decoded = WebUtility.HtmlDecode(withoutTags);
        var normalizedWhitespace = Regex.Replace(decoded, @"[ \t]+", " ");
        var normalizedLines = Regex.Replace(normalizedWhitespace, @"\n{3,}", "\n\n");

        return normalizedLines.Trim();
    }

    public override void Dispose()
    {
        _httpClient.Dispose();
        base.Dispose();
    }

    private sealed class HuduReleaseItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Headline { get; set; }
        public string? Notes { get; set; }
        public string? ReleaseType { get; set; }
        public string? Platform { get; set; }
        public bool Draft { get; set; }
        public string? PublishedDate { get; set; }
        public DateTimeOffset? CreatedAt { get; set; }
        public string? Url { get; set; }
    }
}
