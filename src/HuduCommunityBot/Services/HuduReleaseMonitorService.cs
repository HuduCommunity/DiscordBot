using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    private static readonly string[] DotZeroSectionOrder = ["New Features", "Improvements", "Bug Fixes"];
    private static readonly string[] StandardSectionOrder = ["Improvements", "Bug Fixes", "New Features"];

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

        var relevantReleases = releases
            .Where(r => r.Id > 0)
            .Where(r => !r.Draft)
            .Where(r => string.Equals(r.ReleaseType, "stable", StringComparison.OrdinalIgnoreCase))
            .Where(r => string.Equals(r.Platform, "web", StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => r.Id)
            .ToList();

        if (relevantReleases.Count == 0)
        {
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
            _logger.LogInformation(
                "Observed Hudu release candidate {ReleaseId} ({Version}) published {PublishedAtUtc}.",
                release.Id,
                release.Name,
                ResolveTimestamp(release).UtcDateTime);

            await PostReleaseAsync(release);

            state.LastPostedItemId = release.Id.ToString();
            state.LastCheckedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task PostReleaseAsync(HuduReleaseItem release)
    {
        var discordChannel = await _client.GetChannelAsync(_config.HuduReleaseMonitor.ChannelId);
        if (discordChannel is not IMessageChannel messageChannel)
        {
            _logger.LogWarning(
                "Hudu release monitor channel {ChannelId} was not found or is not a text channel.",
                _config.HuduReleaseMonitor.ChannelId);
            return;
        }

        var releaseUrl = string.IsNullOrWhiteSpace(release.Url)
            ? $"https://hq.hudu.com/releases/{release.Id}.json"
            : release.Url.Trim();

        var parsedNotes = ParseReleaseNotes(release);
        var description = BuildDescription(release, parsedNotes.IntroText);

        var embed = new EmbedBuilder()
            .WithTitle($"Hudu Release {release.Name}")
            .WithColor(Color.Blue)
            .WithUrl(releaseUrl)
            .WithDescription(description)
            .WithFooter("Hudu Releases")
            .WithTimestamp(ResolveTimestamp(release));

        AddSectionFields(embed, release, parsedNotes.Sections);

        string? mentionText = null;
        if (_config.HuduReleaseMonitor.RoleId != 0)
        {
            mentionText = $"<@&{_config.HuduReleaseMonitor.RoleId}>";
        }

        var postedMessage = await messageChannel.SendMessageAsync(text: mentionText, embed: embed.Build());

        if (discordChannel is ITextChannel textChannel)
        {
            await TryCreateThreadAsync(
                textChannel,
                postedMessage,
                BuildThreadName(release.Name, "Release"),
                $"Discussion thread for Hudu release {release.Name}:\n{releaseUrl}");
        }

        _logger.LogInformation("Posted Hudu release update for release ID {ReleaseId} ({Version}).", release.Id, release.Name);
    }

    private async Task TryCreateThreadAsync(ITextChannel channel, IMessage sourceMessage, string threadName, string openerText)
    {
        try
        {
            var thread = await channel.CreateThreadAsync(
                name: threadName,
                type: ThreadType.PublicThread,
                autoArchiveDuration: ThreadArchiveDuration.OneDay,
                message: sourceMessage);

            await thread.SendMessageAsync(openerText);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create thread for release message {MessageId} in channel {ChannelId}.", sourceMessage.Id, channel.Id);
        }
    }

    private static string BuildDescription(HuduReleaseItem release, string introText)
    {
        if (string.IsNullOrWhiteSpace(introText))
        {
            introText = "A new Hudu release is available.";
        }

        introText = Truncate(introText, 800);

        return $"**Version:** `{release.Name}`\n**Release ID:** `{release.Id}`\n\n{introText}";
    }

    private static ParsedReleaseNotes ParseReleaseNotes(HuduReleaseItem release)
    {
        var sourceHtml = !string.IsNullOrWhiteSpace(release.Notes)
            ? release.Notes
            : release.Headline;

        if (string.IsNullOrWhiteSpace(sourceHtml))
        {
            return new ParsedReleaseNotes(string.Empty, []);
        }

        var sections = new List<ReleaseSection>();
        var sectionIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Parse Trix editor section blocks like: <div><strong>Bug Fixes</strong></div><ul>...</ul>
        var sectionMatches = Regex.Matches(
            sourceHtml,
            @"<div[^>]*>\s*(?:<strong>)?\s*(?<heading>[^<]+?)\s*(?:</strong>)?\s*:?\s*</div>\s*<ul[^>]*>(?<items>.*?)</ul>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        foreach (Match sectionMatch in sectionMatches)
        {
            var heading = NormalizeSectionHeading(HtmlToPlainText(sectionMatch.Groups["heading"].Value));
            var headingKey = heading ?? HtmlToPlainText(sectionMatch.Groups["heading"].Value).Trim();
            if (string.IsNullOrWhiteSpace(headingKey))
            {
                continue;
            }

            var listItems = new List<string>();
            var itemMatches = Regex.Matches(
                sectionMatch.Groups["items"].Value,
                @"<li[^>]*>(?<item>.*?)</li>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            foreach (Match itemMatch in itemMatches)
            {
                var itemText = HtmlToPlainText(itemMatch.Groups["item"].Value);
                itemText = itemText.Trim().TrimStart('-', '•').Trim();
                if (!string.IsNullOrWhiteSpace(itemText))
                {
                    listItems.Add(itemText);
                }
            }

            if (listItems.Count == 0)
            {
                continue;
            }

            if (sectionIndex.TryGetValue(headingKey, out var existingIndex))
            {
                sections[existingIndex].Items.AddRange(listItems);
                continue;
            }

            sectionIndex[headingKey] = sections.Count;
            sections.Add(new ReleaseSection(headingKey, listItems));
        }

        var introHtml = sectionMatches.Count > 0
            ? sourceHtml[..sectionMatches[0].Index]
            : sourceHtml;
        var introText = HtmlToPlainText(introHtml);

        return new ParsedReleaseNotes(introText, sections);
    }

    private static void AddSectionFields(EmbedBuilder embed, HuduReleaseItem release, IReadOnlyList<ReleaseSection> sections)
    {
        if (sections.Count == 0)
        {
            return;
        }

        foreach (var section in GetOrderedSections(release.Name, sections))
        {
            embed.AddField(section.Title, BuildSectionFieldValue(section.Items), inline: false);
        }
    }

    private static List<ReleaseSection> GetOrderedSections(string? version, IReadOnlyList<ReleaseSection> sections)
    {
        var orderedTitles = IsDotZeroRelease(version)
            ? DotZeroSectionOrder
            : StandardSectionOrder;

        var remainingSections = new List<ReleaseSection>(sections);
        var orderedSections = new List<ReleaseSection>(sections.Count);

        foreach (var orderedTitle in orderedTitles)
        {
            var section = remainingSections.FirstOrDefault(s =>
                string.Equals(s.Title, orderedTitle, StringComparison.OrdinalIgnoreCase));
            if (section is null)
            {
                continue;
            }

            orderedSections.Add(section);
            remainingSections.Remove(section);
        }

        orderedSections.AddRange(remainingSections);
        return orderedSections;
    }

    private static string BuildSectionFieldValue(IReadOnlyList<string> items)
    {
        var content = string.Join("\n", items.Select(item => $"• {item}"));
        return Truncate(content, 1024);
    }

    private static string? NormalizeSectionHeading(string heading)
    {
        if (string.IsNullOrWhiteSpace(heading))
        {
            return null;
        }

        var normalized = heading.Trim().TrimEnd(':').Trim();
        if (normalized.StartsWith("new feature", StringComparison.OrdinalIgnoreCase))
        {
            return "New Features";
        }

        if (normalized.StartsWith("improvement", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("improved", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("changed", StringComparison.OrdinalIgnoreCase))
        {
            return "Improvements";
        }

        if (normalized.StartsWith("bug fix", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("fix", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("fixed", StringComparison.OrdinalIgnoreCase))
        {
            return "Bug Fixes";
        }

        return normalized;
    }

    private static bool IsDotZeroRelease(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        return Regex.IsMatch(version.Trim(), @"^\d+\.\d+\.0(?:\D.*)?$");
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return value[..(maxLength - 3)].TrimEnd() + "...";
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

    private static string BuildThreadName(string title, string prefix)
    {
        var normalizedTitle = Regex.Replace(title, @"\s+", " ").Trim();
        if (string.IsNullOrWhiteSpace(normalizedTitle))
        {
            normalizedTitle = "Update";
        }

        var candidate = $"{prefix}: {normalizedTitle}";
        return candidate.Length <= 100
            ? candidate
            : candidate[..100].TrimEnd();
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

        [JsonPropertyName("release_type")]
        public string? ReleaseType { get; set; }

        public string? Platform { get; set; }
        public bool Draft { get; set; }

        [JsonPropertyName("published_date")]
        public string? PublishedDate { get; set; }

        [JsonPropertyName("created_at")]
        public DateTimeOffset? CreatedAt { get; set; }

        public string? Url { get; set; }
    }

    private sealed record ParsedReleaseNotes(string IntroText, List<ReleaseSection> Sections);

    private sealed class ReleaseSection
    {
        public string Title { get; }
        public List<string> Items { get; }

        public ReleaseSection(string title, List<string> items)
        {
            Title = title;
            Items = items;
        }
    }

    internal static ReleaseNotesParseResult ParseReleaseNotesForTests(string version, string? notesHtml, string? headlineHtml = null)
    {
        var parsed = ParseReleaseNotes(new HuduReleaseItem
        {
            Name = version,
            Notes = notesHtml,
            Headline = headlineHtml
        });

        var ordered = GetOrderedSections(version, parsed.Sections)
            .Select(section => new ReleaseSectionResult(section.Title, section.Items))
            .ToList();

        return new ReleaseNotesParseResult(parsed.IntroText, ordered);
    }
}

internal sealed record ReleaseNotesParseResult(string IntroText, IReadOnlyList<ReleaseSectionResult> Sections);

internal sealed record ReleaseSectionResult(string Title, IReadOnlyList<string> Items);
