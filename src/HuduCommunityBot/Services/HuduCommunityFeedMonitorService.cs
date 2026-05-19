using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;
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
/// Background service that polls the Hudu Community RSS feed and posts new items to a Discord channel.
/// Last posted item GUID is stored in SQLite via FeedPostState.
/// </summary>
public class HuduCommunityFeedMonitorService : BackgroundService
{
    private const string FeedType = "HuduCommunityRss";

    private readonly DiscordSocketClient _client;
    private readonly BotConfig _config;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<HuduCommunityFeedMonitorService> _logger;
    private readonly HttpClient _httpClient;

    public HuduCommunityFeedMonitorService(
        DiscordSocketClient client,
        BotConfig config,
        IServiceProvider serviceProvider,
        ILogger<HuduCommunityFeedMonitorService> logger)
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
        var monitorConfig = _config.HuduCommunityFeedMonitor;
        if (!monitorConfig.Enabled || monitorConfig.ChannelId == 0)
        {
            _logger.LogInformation("Hudu community feed monitor is disabled or has no channel configured - skipping.");
            return;
        }

        while (_client.ConnectionState != ConnectionState.Connected && !stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }

        _logger.LogInformation(
            "Hudu community feed monitor started. Polling {FeedUrl} every {Interval} minute(s).",
            monitorConfig.FeedUrl,
            monitorConfig.PollIntervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollFeedAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogWarning(ex, "Hudu community feed poll was canceled before completion; monitor will retry on next interval.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error polling Hudu community feed.");
            }

            await Task.Delay(TimeSpan.FromMinutes(monitorConfig.PollIntervalMinutes), stoppingToken);
        }
    }

    private async Task PollFeedAsync(CancellationToken cancellationToken)
    {
        var xml = await _httpClient.GetStringAsync(_config.HuduCommunityFeedMonitor.FeedUrl, cancellationToken);
        var doc = XDocument.Parse(xml);

        var items = ParseItems(doc)
            .OrderBy(x => x.PublishedAt)
            .ThenBy(x => x.StateId, StringComparer.Ordinal)
            .ToList();

        if (items.Count == 0)
        {
            return;
        }

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HuduCommunityBotContext>();
        var sourceId = _config.HuduCommunityFeedMonitor.FeedUrl.Trim();

        var state = await db.FeedPostStates
            .FirstOrDefaultAsync(x => x.FeedType == FeedType && x.SourceId == sourceId, cancellationToken);

        if (state == null)
        {
            var latest = items[^1];
            state = new FeedPostState
            {
                FeedType = FeedType,
                SourceId = sourceId,
                LastPostedItemId = latest.StateId,
                LastCheckedAt = DateTime.UtcNow
            };

            db.FeedPostStates.Add(state);
            await db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Hudu community feed monitor initialized with latest item marker '{LastPostedItemId}'.",
                state.LastPostedItemId);

            return;
        }

        if (string.IsNullOrWhiteSpace(state.LastPostedItemId))
        {
            state.LastPostedItemId = items[^1].StateId;
            state.LastCheckedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Hudu community feed monitor repaired empty state and set marker to '{LastPostedItemId}'.",
                state.LastPostedItemId);

            return;
        }

        var lastIndex = items.FindIndex(x => string.Equals(x.StateId, state.LastPostedItemId, StringComparison.Ordinal));
        if (lastIndex < 0)
        {
            var previousMarker = state.LastPostedItemId;
            var newMarker = items[^1].StateId;
            state.LastPostedItemId = items[^1].StateId;
            state.LastCheckedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            _logger.LogWarning(
                "Hudu community feed marker '{PreviousMarker}' was not found in current feed window. Advanced marker to latest item '{NewMarker}' to avoid duplicate spam.",
                previousMarker,
                newMarker);

            return;
        }

        var pendingItems = items.Skip(lastIndex + 1).ToList();
        if (pendingItems.Count == 0)
        {
            state.LastCheckedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        foreach (var item in pendingItems)
        {
            await PostItemAsync(item);

            state.LastPostedItemId = item.StateId;
            state.LastCheckedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task PostItemAsync(HuduCommunityFeedItem item)
    {
        var discordChannel = await _client.GetChannelAsync(_config.HuduCommunityFeedMonitor.ChannelId);
        if (discordChannel is not IMessageChannel messageChannel)
        {
            _logger.LogWarning(
                "Hudu community feed channel {ChannelId} was not found or is not a message channel.",
                _config.HuduCommunityFeedMonitor.ChannelId);
            return;
        }

        var text = BuildDescription(item);

        var embed = new EmbedBuilder()
            .WithTitle(item.Title)
            .WithColor(Color.Teal)
            .WithUrl(item.Link)
            .WithDescription(text)
            .WithFooter("Hudu Community Feed")
            .WithTimestamp(item.PublishedAt);

        string? mentionText = null;
        if (_config.HuduCommunityFeedMonitor.RoleId != 0)
        {
            mentionText = $"<@&{_config.HuduCommunityFeedMonitor.RoleId}>";
        }

        var postedMessage = await messageChannel.SendMessageAsync(text: mentionText, embed: embed.Build());
        _logger.LogInformation("Posted Hudu community feed item '{Title}'.", item.Title);

        if (discordChannel is ITextChannel textChannel)
        {
            await TryCreateThreadAsync(
                textChannel,
                postedMessage,
                BuildThreadName(item.Title, "Community"),
                $"Discussion thread for this Hudu Community post:\n{item.Link}");
        }
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
            _logger.LogWarning(ex, "Failed to create thread for feed post message {MessageId} in channel {ChannelId}.", sourceMessage.Id, channel.Id);
        }
    }

    private static string BuildDescription(HuduCommunityFeedItem item)
    {
        var sourceText = !string.IsNullOrWhiteSpace(item.ContentHtml)
            ? item.ContentHtml
            : item.DescriptionHtml;

        var text = HtmlToPlainText(sourceText);
        if (string.IsNullOrWhiteSpace(text))
        {
            text = "New post published on Hudu Community.";
        }

        if (text.Length > 900)
        {
            text = text[..900].TrimEnd() + "...";
        }

        var categoryText = item.Categories.Count > 0
            ? string.Join(", ", item.Categories)
            : "None";

        return $"**Author:** {item.Author}\n**Categories:** {categoryText}\n\n{text}";
    }

    private static string BuildThreadName(string title, string prefix)
    {
        var normalizedTitle = Regex.Replace(title, @"\s+", " ").Trim();
        if (string.IsNullOrWhiteSpace(normalizedTitle))
        {
            normalizedTitle = "Post";
        }

        var candidate = $"{prefix}: {normalizedTitle}";
        return candidate.Length <= 100
            ? candidate
            : candidate[..100].TrimEnd();
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

    private static List<HuduCommunityFeedItem> ParseItems(XDocument doc)
    {
        var nsContent = XNamespace.Get("http://purl.org/rss/1.0/modules/content/");
        var nsDc = XNamespace.Get("http://purl.org/dc/elements/1.1/");

        var items = new List<HuduCommunityFeedItem>();

        foreach (var itemElement in doc.Descendants("item"))
        {
            var title = itemElement.Element("title")?.Value?.Trim() ?? "Hudu Community Post";
            var link = itemElement.Element("link")?.Value?.Trim();
            var guid = itemElement.Element("guid")?.Value?.Trim();
            var author = itemElement.Element(nsDc + "creator")?.Value?.Trim() ?? "Unknown";
            var descriptionHtml = itemElement.Element("description")?.Value ?? string.Empty;
            var contentHtml = itemElement.Element(nsContent + "encoded")?.Value ?? string.Empty;

            if (string.IsNullOrWhiteSpace(link))
            {
                continue;
            }

            var pubDateRaw = itemElement.Element("pubDate")?.Value;
            var publishedAt = DateTimeOffset.UtcNow;
            if (!string.IsNullOrWhiteSpace(pubDateRaw) && DateTimeOffset.TryParse(pubDateRaw, out var parsed))
            {
                publishedAt = parsed;
            }

            var categories = itemElement.Elements("category")
                .Select(x => x.Value.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var stateId = !string.IsNullOrWhiteSpace(guid)
                ? guid
                : link;

            items.Add(new HuduCommunityFeedItem
            {
                StateId = stateId,
                Title = title,
                Link = link,
                Author = author,
                DescriptionHtml = descriptionHtml,
                ContentHtml = contentHtml,
                PublishedAt = publishedAt,
                Categories = categories
            });
        }

        return items;
    }

    public override void Dispose()
    {
        _httpClient.Dispose();
        base.Dispose();
    }

    private sealed class HuduCommunityFeedItem
    {
        public string StateId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Link { get; set; } = string.Empty;
        public string Author { get; set; } = "Unknown";
        public string DescriptionHtml { get; set; } = string.Empty;
        public string ContentHtml { get; set; } = string.Empty;
        public DateTimeOffset PublishedAt { get; set; }
        public List<string> Categories { get; set; } = [];
    }
}
