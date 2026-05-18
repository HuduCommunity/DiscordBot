namespace DiscordBot.Models;

public class HuduReleaseMonitorConfig
{
    public bool Enabled { get; set; } = false;

    public ulong ChannelId { get; set; }

    public ulong RoleId { get; set; }

    public string FeedUrl { get; set; } = "https://hq.hudu.com/public/releases.json";

    public int PollIntervalMinutes { get; set; } = 15;

    public int BaselineReleaseId { get; set; } = 67;

    /// <summary>
    /// Validates the Hudu release monitor configuration.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when validation fails.</exception>
    public void Validate()
    {
        if (Enabled)
        {
            if (ChannelId == 0)
                throw new InvalidOperationException("HuduReleaseMonitorConfig: When enabled, ChannelId must be a valid Discord channel ID (non-zero). Check HUDUCOMMUNITYBOT_Bot__HuduReleaseMonitor__ChannelId.");

            if (string.IsNullOrWhiteSpace(FeedUrl) || !Uri.TryCreate(FeedUrl, UriKind.Absolute, out _))
                throw new InvalidOperationException($"HuduReleaseMonitorConfig: FeedUrl must be a valid URL. Current value: '{FeedUrl}'.");

            if (PollIntervalMinutes <= 0)
                throw new InvalidOperationException($"HuduReleaseMonitorConfig: PollIntervalMinutes must be greater than 0. Current value: {PollIntervalMinutes}.");

            if (BaselineReleaseId <= 0)
                throw new InvalidOperationException($"HuduReleaseMonitorConfig: BaselineReleaseId must be greater than 0. Current value: {BaselineReleaseId}.");
        }
    }
}
