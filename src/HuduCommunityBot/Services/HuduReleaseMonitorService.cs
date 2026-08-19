using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
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
                _logger.LogInformation(ex, "Hudu release feed poll timed out or was canceled; monitor will retry on next interval.");
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

        var baselineReleaseId = _config.HuduReleaseMonitor.BaselineReleaseId;
        var lastPostedReleaseId = ResolveLastPostedReleaseId(state?.LastPostedItemId, baselineReleaseId);

        if (state == null)
        {
            state = new FeedPostState
            {
                FeedType = FeedType,
                SourceId = sourceId,
                LastPostedItemId = lastPostedReleaseId.ToString(),
                LastCheckedAt = DateTime.UtcNow
            };

            db.FeedPostStates.Add(state);
            await db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Hudu release monitor initialized with baseline release ID {BaselineReleaseId}.",
                lastPostedReleaseId);
        }
        else if (!int.TryParse(state.LastPostedItemId, out var parsedLastPostedReleaseId) || parsedLastPostedReleaseId <= 0)
        {
            lastPostedReleaseId = baselineReleaseId;
            state.LastPostedItemId = lastPostedReleaseId.ToString();
            state.LastCheckedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Hudu release monitor repaired invalid state and set baseline release ID to {BaselineReleaseId}.",
                lastPostedReleaseId);
        }
        else
        {
            lastPostedReleaseId = parsedLastPostedReleaseId;
        }

        var newReleases = relevantReleases
            .Where(r => r.Id > lastPostedReleaseId)
            .OrderBy(r => r.Id)
            .ToList();

        var discussionFeedMatches = await LoadDiscussionFeedMatchesAsync();

        foreach (var release in newReleases)
        {
            _logger.LogInformation(
                "Observed Hudu release candidate {ReleaseId} ({Version}) published {PublishedAtUtc}.",
                release.Id,
                release.Name,
                ResolveTimestamp(release).UtcDateTime);

            var communityPost = await TryFindCommunityReleasePostAsync(release, discussionFeedMatches);
            await PostReleaseAsync(release, communityPost);

            state.LastPostedItemId = release.Id.ToString();
            state.LastCheckedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }

        var processedReleaseIds = new HashSet<int>(newReleases.Select(r => r.Id));
        var retrospectiveUpperBoundReleaseId = relevantReleases.Max(r => r.Id);
        var retrospectiveUpdates = GetRetrospectiveUpdateReleases(relevantReleases, _config.HuduReleaseMonitor.BaselineReleaseId, retrospectiveUpperBoundReleaseId)
            .Where(release => !processedReleaseIds.Contains(release.Id))
            .ToList();

        if (retrospectiveUpdates.Count == 0)
        {
            state.LastCheckedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var releaseChannel = await _client.GetChannelAsync(_config.HuduReleaseMonitor.ChannelId);
        if (releaseChannel is ITextChannel textChannel)
        {
            foreach (var release in retrospectiveUpdates.OrderBy(r => r.Id))
            {
                var communityPost = await TryFindCommunityReleasePostAsync(release, discussionFeedMatches);
                await TryUpdateExistingReleasePostAsync(textChannel, release, communityPost);
            }
        }
    }

    private static int ResolveLastPostedReleaseId(string? lastPostedItemId, int baselineReleaseId)
    {
        if (int.TryParse(lastPostedItemId, out var lastPostedReleaseId) && lastPostedReleaseId > 0)
        {
            return lastPostedReleaseId;
        }

        return baselineReleaseId;
    }

    private static List<HuduReleaseItem> GetRetrospectiveUpdateReleases(IReadOnlyList<HuduReleaseItem> releases, int baselineReleaseId, int lastPostedReleaseId)
    {
        if (releases.Count == 0)
        {
            return [];
        }

        var releaseIds = releases
            .Select(r => r.Id)
            .Where(id => id > 0)
            .OrderBy(id => id)
            .ToList();

        if (releaseIds.Count == 0)
        {
            return [];
        }

        var relevantIds = releaseIds
            .Where(id => id >= baselineReleaseId && id <= lastPostedReleaseId)
            .TakeLast(3)
            .ToList();

        return releases
            .Where(r => relevantIds.Contains(r.Id))
            .OrderBy(r => r.Id)
            .ToList();
    }

    private async Task PostReleaseAsync(HuduReleaseItem release, HuduCommunityPostMatch? communityPost)
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
            ? null
            : release.Url.Trim();
        var displayUrl = ResolveReleaseDisplayUrl(releaseUrl, communityPost?.Link);

        var parsedNotes = ParseReleaseNotes(release);
        var embed = BuildReleaseEmbed(release, releaseUrl, displayUrl, parsedNotes, communityPost?.Link);

        string? mentionText = null;
        if (_config.HuduReleaseMonitor.RoleId != 0)
        {
            mentionText = $"<@&{_config.HuduReleaseMonitor.RoleId}>";
        }

        var postedMessage = await messageChannel.SendMessageAsync(text: mentionText, embed: embed);

        if (discordChannel is ITextChannel textChannel)
        {
            await TryCreateThreadAsync(
                textChannel,
                postedMessage,
                BuildThreadName(release.Name, "Release"),
                BuildThreadOpenerText(release, communityPost));
        }

        _logger.LogInformation("Posted Hudu release update for release ID {ReleaseId} ({Version}).", release.Id, release.Name);
    }

    private async Task TryUpdateExistingReleasePostAsync(ITextChannel channel, HuduReleaseItem release, HuduCommunityPostMatch? communityPost)
    {
        if (communityPost is null)
        {
            return;
        }

        var recentMessages = await channel.GetMessagesAsync(100).FlattenAsync();
        var matchingMessage = recentMessages
            .Where(message => message.Author.Id == _client.CurrentUser.Id)
            .Where(message => message.Embeds.Count > 0)
            .Select(message => new { Message = message, Embed = message.Embeds.FirstOrDefault() })
            .FirstOrDefault(entry =>
                entry.Embed is not null &&
                string.Equals(entry.Embed.Title, $"Hudu Release {release.Name}", StringComparison.Ordinal));

        if (matchingMessage is null)
        {
            return;
        }

        var displayUrl = ResolveReleaseDisplayUrl(string.IsNullOrWhiteSpace(release.Url) ? null : release.Url.Trim(), communityPost.Link);
        var parsedNotes = ParseReleaseNotes(release);
        var updatedEmbed = BuildReleaseEmbed(release, string.IsNullOrWhiteSpace(release.Url) ? null : release.Url.Trim(), displayUrl, parsedNotes, communityPost.Link);

        if (!ShouldRetroactivelyUpdateExistingReleasePost(matchingMessage.Embed?.Url, displayUrl))
        {
            await TryPostCommunityThreadUpdateAsync(channel, release, communityPost);
            return;
        }

        await ((IUserMessage)matchingMessage.Message).ModifyAsync(properties => properties.Embed = updatedEmbed);
        await TryPostCommunityThreadUpdateAsync(channel, release, communityPost);
    }

    private static bool ShouldRetroactivelyUpdateExistingReleasePost(string? currentUrl, string? desiredUrl)
    {
        if (string.IsNullOrWhiteSpace(desiredUrl))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(currentUrl))
        {
            return true;
        }

        if (string.Equals(currentUrl, desiredUrl, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var currentLower = currentUrl.Trim().ToLowerInvariant();
        var desiredLower = desiredUrl.Trim().ToLowerInvariant();

        if (desiredLower.Contains("reddit.com", StringComparison.OrdinalIgnoreCase) &&
            currentLower.Contains("community.hudu.com", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return true;
    }

    private async Task<HuduCommunityPostMatch?> TryFindCommunityReleasePostAsync(HuduReleaseItem release, IReadOnlyList<HuduCommunityPostMatch>? discussionFeedMatches)
    {
        if (discussionFeedMatches is not null)
        {
            var matchedItem = FindMatchingCommunityPost(release.Name, discussionFeedMatches);
            if (matchedItem is null)
            {
                _logger.LogInformation(
                    "No matching Hudu Community release post found for version {Version}.",
                    release.Name);
                return null;
            }

            _logger.LogInformation(
                "Matched Hudu Community release post for version {Version}: {CommunityPostUrl}",
                release.Name,
                matchedItem.Link);

            return matchedItem;
        }

        var feedUrl = ResolveDiscussionFeedUrl(_config);
        if (string.IsNullOrWhiteSpace(feedUrl) || !Uri.TryCreate(feedUrl, UriKind.Absolute, out _))
        {
            return null;
        }

        try
        {
            var xml = await _httpClient.GetStringAsync(feedUrl);
            var doc = XDocument.Parse(xml);
            var items = ParseCommunityItems(doc);
            var matchedItem = FindMatchingCommunityPost(release.Name, items);
            if (matchedItem is null)
            {
                _logger.LogInformation(
                    "No matching Hudu Community release post found for version {Version}.",
                    release.Name);
                return null;
            }

            _logger.LogInformation(
                "Matched Hudu Community release post for version {Version}: {CommunityPostUrl}",
                release.Name,
                matchedItem.Link);

            return matchedItem;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve Hudu Community release post for version {Version}.", release.Name);
            return null;
        }
    }

    private async Task<IReadOnlyList<HuduCommunityPostMatch>> LoadDiscussionFeedMatchesAsync()
    {
        var feedUrl = ResolveDiscussionFeedUrl(_config);
        if (string.IsNullOrWhiteSpace(feedUrl) || !Uri.TryCreate(feedUrl, UriKind.Absolute, out _))
        {
            return [];
        }

        try
        {
            var xml = await _httpClient.GetStringAsync(feedUrl);
            var doc = XDocument.Parse(xml);
            return ParseCommunityItems(doc);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load discussion feed matches for Hudu release posts.");
            return [];
        }
    }

    private static string? ResolveDiscussionFeedUrl(BotConfig config)
    {
        return config.HuduReleaseMonitor.DiscussionFeedUrl?.Trim();
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
            await thread.ModifyAsync(properties =>
            {
                properties.Archived = true;
                properties.Locked = true;
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create thread for release message {MessageId} in channel {ChannelId}.", sourceMessage.Id, channel.Id);
        }
    }

    private static Embed BuildReleaseEmbed(HuduReleaseItem release, string? releaseUrl, string? displayUrl, ParsedReleaseNotes parsedNotes, string? communityPostUrl)
    {
        var description = BuildDescription(release, parsedNotes.IntroText, communityPostUrl);

        var embedBuilder = new EmbedBuilder()
            .WithTitle($"Hudu Release {release.Name}")
            .WithColor(Color.Blue)
            .WithDescription(description)
            .WithFooter("Hudu Releases")
            .WithTimestamp(ResolveTimestamp(release));

        if (!string.IsNullOrWhiteSpace(displayUrl))
        {
            embedBuilder.WithUrl(displayUrl);
        }

        AddSectionFields(embedBuilder, release, parsedNotes.Sections);
        return embedBuilder.Build();
    }

    private static string BuildDescription(HuduReleaseItem release, string introText, string? communityPostUrl)
    {
        if (string.IsNullOrWhiteSpace(introText))
        {
            introText = "A new Hudu release is available.";
        }

        introText = Truncate(introText, 800);

        var communityLine = string.IsNullOrWhiteSpace(communityPostUrl)
            ? string.Empty
            : $"\n**Community Post:** {communityPostUrl}";

        return $"**Version:** `{release.Name}`\n**Release ID:** `{release.Id}`{communityLine}\n\n{introText}";
    }

    private static string BuildThreadOpenerText(HuduReleaseItem release, HuduCommunityPostMatch? communityPost)
    {
        if (communityPost is null)
        {
            return $"Discussion thread for Hudu release {release.Name}.";
        }

        return $"Community discussion thread for Release {release.Name}: {communityPost.Link}";
    }

    private static string? ResolveReleaseDisplayUrl(string? releaseUrl, string? communityPostUrl)
    {
        var displayUrl = string.IsNullOrWhiteSpace(communityPostUrl)
            ? releaseUrl
            : communityPostUrl;

        return string.IsNullOrWhiteSpace(displayUrl) ? null : displayUrl.Trim();
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

    private static List<HuduCommunityPostMatch> ParseCommunityItems(XDocument doc)
    {
        var items = new List<HuduCommunityPostMatch>();

        foreach (var itemElement in doc.Descendants().Where(element =>
                     string.Equals(element.Name.LocalName, "item", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(element.Name.LocalName, "entry", StringComparison.OrdinalIgnoreCase)))
        {
            var title = ExtractElementValue(itemElement, "title");
            var link = ExtractLinkValue(itemElement);

            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(link))
            {
                continue;
            }

            items.Add(new HuduCommunityPostMatch(title.Trim(), link.Trim()));
        }

        return items;
    }

    private async Task TryPostCommunityThreadUpdateAsync(ITextChannel channel, HuduReleaseItem release, HuduCommunityPostMatch communityPost)
    {
        var threadName = BuildThreadName(release.Name, "Release");
        var thread = (await channel.GetActiveThreadsAsync()).FirstOrDefault(existingThread =>
            string.Equals(existingThread.Name, threadName, StringComparison.Ordinal));

        if (thread is null)
        {
            return;
        }

        var mentionText = _config.HuduReleaseMonitor.RoleId != 0
            ? $"<@&{_config.HuduReleaseMonitor.RoleId}>"
            : null;

        var threadMessage = $"{mentionText} Community discussion thread for Release {release.Name}: {communityPost.Link}".Trim();
        var recentMessages = await thread.GetMessagesAsync(50).FlattenAsync();
        if (recentMessages.Any(message => HasMatchingCommunityAnnouncement(message.Content, release.Name, communityPost.Link)))
        {
            return;
        }

        await thread.SendMessageAsync(threadMessage);
    }

    private static bool HasMatchingCommunityAnnouncement(string? content, string? releaseVersion, string? communityPostLink)
    {
        if (string.IsNullOrWhiteSpace(content) || string.IsNullOrWhiteSpace(releaseVersion) || string.IsNullOrWhiteSpace(communityPostLink))
        {
            return false;
        }

        var normalizedContent = content.Trim();
        if (!normalizedContent.Contains($"release {releaseVersion}", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var targetUrl = NormalizeUrlForComparison(communityPostLink);
        if (string.IsNullOrWhiteSpace(targetUrl))
        {
            return false;
        }

        var urls = Regex.Matches(normalizedContent, @"https?://\S+", RegexOptions.IgnoreCase)
            .Select(match => NormalizeUrlForComparison(match.Value.TrimEnd('.', ',', ';', ')', ']', '>', '"', '\'')))
            .Where(url => !string.IsNullOrWhiteSpace(url));

        return urls.Any(url => string.Equals(url, targetUrl, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeUrlForComparison(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        var trimmed = url.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var parsedUri))
        {
            return trimmed.TrimEnd('/').ToLowerInvariant();
        }

        var uriBuilder = new UriBuilder(parsedUri)
        {
            Fragment = string.Empty
        };

        var path = uriBuilder.Uri.GetLeftPart(UriPartial.Path).TrimEnd('/').ToLowerInvariant();
        var query = uriBuilder.Query;

        if (string.IsNullOrWhiteSpace(query))
        {
            return path;
        }

        return string.Concat(path, query.ToLowerInvariant());
    }

    private static string? ExtractElementValue(XElement parent, string elementName)
    {
        var element = parent.Elements().FirstOrDefault(child => string.Equals(child.Name.LocalName, elementName, StringComparison.OrdinalIgnoreCase));
        if (element is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(element.Value))
        {
            return element.Value;
        }

        return element.Nodes().OfType<XCData>().Select(node => node.Value).FirstOrDefault();
    }

    private static string? ExtractLinkValue(XElement parent)
    {
        var linkElement = parent.Elements().FirstOrDefault(element => string.Equals(element.Name.LocalName, "link", StringComparison.OrdinalIgnoreCase));
        if (linkElement is null)
        {
            return null;
        }

        var href = linkElement.Attribute("href")?.Value;
        if (!string.IsNullOrWhiteSpace(href))
        {
            return href.Trim();
        }

        var url = linkElement.Value;
        if (!string.IsNullOrWhiteSpace(url))
        {
            return url.Trim();
        }

        return null;
    }

    private static HuduCommunityPostMatch? FindMatchingCommunityPost(string? releaseVersion, IReadOnlyList<HuduCommunityPostMatch> items)
    {
        var normalizedVersion = NormalizeVersionToken(releaseVersion);
        if (string.IsNullOrWhiteSpace(normalizedVersion))
        {
            return null;
        }

        foreach (var item in items)
        {
            var normalizedTitle = NormalizeTitleForMatching(item.Title);
            if (normalizedTitle.Length == 0)
            {
                continue;
            }

            if (!IsCoreHuduReleaseTitle(normalizedTitle, normalizedVersion))
            {
                continue;
            }

            return item;
        }

        return null;
    }

    private static string NormalizeVersionToken(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return string.Empty;
        }

        var match = Regex.Match(version, @"\d+(?:\.\d+)+");
        return match.Success ? match.Value : string.Empty;
    }

    private static string NormalizeTitleForMatching(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var normalized = WebUtility.HtmlDecode(title).Trim().ToLowerInvariant();
        normalized = Regex.Replace(normalized, @"[^a-z0-9\.]+", " ");
        normalized = Regex.Replace(normalized, @"\s+", " ").Trim();

        return normalized;
    }

    private static bool IsCoreHuduReleaseTitle(string normalizedTitle, string normalizedVersion)
    {
        if (string.IsNullOrWhiteSpace(normalizedTitle) || string.IsNullOrWhiteSpace(normalizedVersion))
        {
            return false;
        }

        var versionToken = normalizedVersion.Replace(".", "");
        var corePatterns = new[]
        {
            $"hudu version {normalizedVersion}",
            $"hudu {normalizedVersion}",
            $"hudu version {normalizedVersion} release notes",
            $"hudu {normalizedVersion} release notes",
            $"v{normalizedVersion} is live",
            $"v{normalizedVersion} live",
            $"v{versionToken} is live",
            $"v{versionToken} live",
            $"{normalizedVersion} is live",
            $"{normalizedVersion} live",
            $"{normalizedVersion} is live!",
            $"{normalizedVersion} live!"
        };

        return corePatterns.Any(pattern => string.Equals(normalizedTitle, pattern, StringComparison.Ordinal));
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

    internal static string? FindCommunityPostLinkForTests(string? releaseVersion, IReadOnlyList<(string Title, string Link)> items)
    {
        var normalizedItems = items
            .Select(item => new HuduCommunityPostMatch(item.Title, item.Link))
            .ToList();

        return FindMatchingCommunityPost(releaseVersion, normalizedItems)?.Link;
    }

    internal static string? ResolveReleaseDisplayUrlForTests(string? releaseUrl, string? communityPostUrl)
    {
        return ResolveReleaseDisplayUrl(releaseUrl, communityPostUrl);
    }

    internal static HuduCommunityPostMatch? FindMatchingDiscussionPostForTests(string? releaseVersion, IReadOnlyList<HuduCommunityPostMatch> items)
    {
        return FindMatchingCommunityPost(releaseVersion, items);
    }

    internal static string? ResolveDiscussionFeedUrlForTests(BotConfig config)
    {
        return ResolveDiscussionFeedUrl(config);
    }

    internal static bool ShouldRetroactivelyUpdateExistingReleasePostForTests(string? currentUrl, string? desiredUrl)
    {
        return ShouldRetroactivelyUpdateExistingReleasePost(currentUrl, desiredUrl);
    }

    internal static bool HasMatchingCommunityAnnouncementForTests(string? content, string? releaseVersion, string? communityPostLink)
    {
        return HasMatchingCommunityAnnouncement(content, releaseVersion, communityPostLink);
    }

    internal static int ResolveLastPostedReleaseIdForTests(string? lastPostedItemId, int baselineReleaseId)
    {
        return ResolveLastPostedReleaseId(lastPostedItemId, baselineReleaseId);
    }

    internal static List<int> GetRetrospectiveUpdateReleaseIdsForTests(IReadOnlyList<int> releaseIds, int baselineReleaseId, int lastPostedReleaseId)
    {
        var releases = releaseIds.Select(id => new HuduReleaseItem { Id = id }).ToList();
        return GetRetrospectiveUpdateReleases(releases, baselineReleaseId, lastPostedReleaseId)
            .Select(release => release.Id)
            .ToList();
    }

    internal static List<HuduCommunityPostMatch> ParseCommunityItemsForTests(string xml)
    {
        var doc = XDocument.Parse(xml);
        return ParseCommunityItems(doc);
    }
}

internal sealed record ReleaseNotesParseResult(string IntroText, IReadOnlyList<ReleaseSectionResult> Sections);

internal sealed record ReleaseSectionResult(string Title, IReadOnlyList<string> Items);

internal sealed record HuduCommunityPostMatch(string Title, string Link);
