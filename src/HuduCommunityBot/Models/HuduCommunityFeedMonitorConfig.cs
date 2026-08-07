namespace DiscordBot.Models;

public class HuduCommunityFeedMonitorConfig
{
    public bool Enabled { get; set; } = false;

    public ulong ChannelId { get; set; }

    public ulong RoleId { get; set; }

    public string FeedUrl { get; set; } = "https://community.hudu.com/rss/feed";

    public int PollIntervalMinutes { get; set; } = 15;

    /// <summary>
    /// Validates the Hudu community feed monitor configuration.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when validation fails.</exception>
    public void Validate()
    {
        if (Enabled)
        {
            if (ChannelId == 0)
                throw new InvalidOperationException("HuduCommunityFeedMonitorConfig: When enabled, ChannelId must be a valid Discord channel ID (non-zero). Check HUDUCOMMUNITYBOT_Bot__HuduCommunityFeedMonitor__ChannelId.");

            if (string.IsNullOrWhiteSpace(FeedUrl) || !Uri.TryCreate(FeedUrl, UriKind.Absolute, out _))
                throw new InvalidOperationException($"HuduCommunityFeedMonitorConfig: FeedUrl must be a valid URL. Current value: '{FeedUrl}'.");

            if (PollIntervalMinutes <= 0)
                throw new InvalidOperationException($"HuduCommunityFeedMonitorConfig: PollIntervalMinutes must be greater than 0. Current value: {PollIntervalMinutes}.");
        }
    }
}
