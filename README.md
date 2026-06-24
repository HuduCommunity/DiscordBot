# HuduCommunity Bot

Discord bot for the Hudu Community server, built with C# (.NET 10) and [Discord.Net](https://github.com/discord-net/Discord.Net).

## ✨ Features

* **Slash commands** via Discord.Net's `InteractionService`
* **Moderation tools**: ban, kick, mute, warn, clear, purge, slowmode, lock/unlock
* **General utilities**: avatar, userinfo, serverinfo, reminders, fun commands, and more
* **Hudu release monitor**: polls [Hudu releases JSON feed](https://hq.hudu.com/public/releases.json), posts new stable web releases, and auto-creates a discussion thread for each release post
* **Hudu community feed monitor**: polls [Hudu Community RSS feed](https://community.hudu.com/rss/feed), posts new community items, and auto-creates a discussion thread for each post
* **Single-message channel enforcement**: restricts designated channels to one message per user, with slash commands to enable/disable enforcement and reset individual users
* **Moderation action logging**: posts a rich embed to a configured forum channel for every moderation action (ban, unban, kick, mute, unmute, warn, clear, purge, lock/unlock, slowmode, and automated single-message deletions)
* **Cross-channel spam detection**: flags users who post identical messages across multiple channels within a configurable time window, alerting moderators with interactive ban/dismiss buttons
* **Permission-aware error handling**: friendly ephemeral responses when permission checks fail
* **Deployment via GitHub Actions**: CI build gate → SSH deploy to Linux host with systemd

## 🚀 Getting Started

### Prerequisites

* [.NET 10 SDK](https://dotnet.microsoft.com/download)
* A Discord bot token ([How to create a bot](https://discord.com/developers/applications))

### Local Development

1. **Clone the repository:**

```bash
git clone https://github.com/HuduCommunity/DiscordBot.git
cd DiscordBot
```

1. **Configure the bot** using one of:

* `src/HuduCommunityBot/appsettings.Development.json` (gitignored)
* .NET User Secrets: `dotnet user-secrets set "Bot:Token" "your-token-here" --project src/HuduCommunityBot`

1. **Build and run:**

```bash
dotnet run --project src/HuduCommunityBot
```

In `Debug` builds, slash commands are registered to the guild specified by `Bot:GuildId` for instant availability. Release builds register commands globally.

### Required Bot Permissions

The bot requires the following permissions (the invite URL should include these):

* Read Messages / View Channels
* Send Messages
* Embed Links
* Manage Messages
* Kick Members
* Ban Members
* Moderate Members (for timeout/mute)
* Manage Channels (for lock/slowmode)
* Create Public Threads (for moderation log forum posts)

> **Note:** The **Message Content** privileged intent must be enabled in the [Discord Developer Portal](https://discord.com/developers/applications) for the single-message enforcement and cross-channel spam detection features to function. Restart the bot after enabling it — no token refresh is required.

## 📖 Commands

### General

| Command | Description |
| --- | --- |
| `/about` | Shows bot information and uptime |
| `/avatar [user]` | Displays a user's avatar |
| `/fun` | Random fun commands |
| `/help` | Lists all available commands |
| `/ping` | Shows bot latency |
| `/remind <time> <message>` | Sets a reminder |
| `/serverinfo` | Shows server information |
| `/userinfo [user]` | Shows information about a user |

### Moderation

| Command | Required User Permission | Required Bot Permission |
| --- | --- | --- |
| `/ban <user> [reason]` | Ban Members | Ban Members |
| `/unban <userid>` | Ban Members | Ban Members |
| `/kick <user> [reason]` | Kick Members | Kick Members |
| `/mute <user> <duration> [reason]` | Moderate Members | Moderate Members |
| `/unmute <user>` | Moderate Members | Moderate Members |
| `/warn <user> <reason>` | Kick Members | Kick Members |
| `/warnings <user>` | Manage Messages | — |
| `/clear <amount>` | Manage Messages | Manage Messages |
| `/purge_user <user> <amount>` | Manage Messages | Manage Messages |
| `/lock [channel]` | Manage Channels | Manage Channels |
| `/unlock [channel]` | Manage Channels | Manage Channels |
| `/slowmode <seconds>` | Manage Channels | Manage Channels |

> **Note:** `/warn` auto-kicks a user after 3 accumulated warnings.

### Single-Message Enforcement

These commands require the **Manage Channels** permission.

| Command | Description |
| --- | --- |
| `/singlemessage enable [channel]` | Enable single-message enforcement for a channel (defaults to current channel) |
| `/singlemessage disable [channel]` | Disable enforcement for a channel; existing records are retained |
| `/singlemessage reset-user <user> [channel]` | Remove a user's record so they may post again |
| `/singlemessage list [channel]` | List all users who have posted in the channel, with links to their messages |

## ⚙️ Configuration

All settings live under the `Bot` key in `appsettings.json`:

```json
{
  "Bot": {
    "Token": "",
    "Prefix": "!",
    "GuildId": 0,
    "AllowPrefixCommands": false,
    "AllowedFunChannels": [],
    "Cooldowns": {
      "UserInfo": 5,
      "Status": 15
    },
    "StatusMonitor": {
      "Enabled": false,
      "ChannelId": 0,
      "RoleId": 0,
      "FeedUrl": "https://status.haloservicesolutions.com/pages/63ef45da7ee94905308a1a4a/rss",
      "PollIntervalMinutes": 5
    },
    "HuduReleaseMonitor": {
      "Enabled": false,
      "ChannelId": 0,
      "RoleId": 0,
      "FeedUrl": "https://hq.hudu.com/public/releases.json",
      "PollIntervalMinutes": 15,
      "BaselineReleaseId": 67
    },
    "HuduCommunityFeedMonitor": {
      "Enabled": false,
      "ChannelId": 0,
      "RoleId": 0,
      "FeedUrl": "https://community.hudu.com/rss/feed",
      "PollIntervalMinutes": 15
    },
    "YoutubeMonitor": {
      "Enabled": false,
      "ForumChannelId": 0,
      "RoleId": 0,
      "YouTubeDataApiKey": "",
      "PollIntervalMinutes": 60,
      "DefaultPostTitleTemplate": "[{ChannelName}] {VideoTitle}",
      "Channels": []
    },
    "Heartbeat": {
      "Enabled": false,
      "PushUrl": "",
      "IntervalSeconds": 60,
      "StartupDelaySeconds": 15,
      "TimeoutSeconds": 10
    },
    "SingleMessage": {
      "Channels": []
    }
  },
  "ModerationLog": {
    "ForumChannelId": 0,
    "ModeratorRoleId": 0
  },
  "CrossChannelSpam": {
    "Enabled": false,
    "TimeWindowSeconds": 30,
    "MinimumChannelCount": 3,
    "DeleteMessages": true,
    "TimeoutOnDetection": true
  }
}
```

### Status Monitor

Set `StatusMonitor:Enabled` to `true` and configure:

| Setting | Description |
| --- | --- |
| `ChannelId` | Channel where status updates are posted |
| `RoleId` | Role to mention on status updates (set `0` to disable mentions) |
| `FeedUrl` | RSS feed URL (defaults to Halo Services Solutions) |
| `PollIntervalMinutes` | How often to check for new feed items (default: 5) |

### Hudu Release Monitor

Set `HuduReleaseMonitor:Enabled` to `true` and configure:

| Setting | Description |
| --- | --- |
| `ChannelId` | Channel where release updates are posted |
| `RoleId` | Optional role to mention on release updates (set `0` to disable mentions) |
| `FeedUrl` | Releases feed URL (defaults to `https://hq.hudu.com/public/releases.json`) |
| `PollIntervalMinutes` | How often to check for new releases (default: 15) |
| `BaselineReleaseId` | Initial last-posted release ID for first run bootstrap (default: 67) |

Release state is persisted in SQLite using `FeedPostState` (`FeedType = HuduRelease`), so the monitor continues from the last posted release ID after restarts.

Each release post also attempts to create a public thread in the same channel for follow-up discussion.

### Hudu Community Feed Monitor

Set `HuduCommunityFeedMonitor:Enabled` to `true` and configure:

| Setting | Description |
| --- | --- |
| `ChannelId` | Channel where community feed updates are posted |
| `RoleId` | Optional role to mention on community updates (set `0` to disable mentions) |
| `FeedUrl` | Community RSS URL (defaults to `https://community.hudu.com/rss/feed`) |
| `PollIntervalMinutes` | How often to check for new feed items (default: 15) |

Community feed state is persisted in SQLite using `FeedPostState` (`FeedType = HuduCommunityRss`) with item GUID/link markers.

On first run, the monitor stores the latest current item as baseline to avoid posting historical backlog.

### YouTube Monitor

Set `YoutubeMonitor:Enabled` to `true` and configure:

| Setting | Description |
| --- | --- |
| `ForumChannelId` | Discord forum channel ID where new video threads are created |
| `RoleId` | Optional role to mention when a new video is posted (set `0` to disable mentions) |
| `YouTubeDataApiKey` | Optional YouTube Data API key used to resolve plain channel names to channel IDs |
| `PollIntervalMinutes` | Feed polling cadence (default: 15) |
| `DefaultPostTitleTemplate` | Thread title template (supports `{ChannelName}` and `{VideoTitle}`) |
| `DefaultPostBodyTemplate` | Forum post body template (supports placeholders listed below; default: `New video from **{ChannelName}**\n{VideoUrl}`) |
| `Channels` | Optional startup seed list of YouTube channel IDs, @handles, feed URLs, or channel names (channel names require `YouTubeDataApiKey`) |

#### YouTube Title Template Variables

The following placeholders are available in both:

* `DefaultPostTitleTemplate`
* per-channel `PostTitleTemplate` (for tracked channels)
* `DefaultPostBodyTemplate`

| Variable | Meaning | Example Value |
| --- | --- | --- |
| `{ChannelName}` | Display name of the YouTube channel for the post | `Hudu Community` |
| `{ChannelId}` | Tracked channel reference (YouTube channel ID) | `UC1234567890abcdef` |
| `{VideoTitle}` | Title of the YouTube video | `Hudu Version 2.42.1` |
| `{VideoId}` | YouTube video ID | `dQw4w9WgXcQ` |
| `{VideoUrl}` | Full YouTube watch URL | `https://www.youtube.com/watch?v=dQw4w9WgXcQ` |
| `{PublishedDate}` | Video publish date in UTC (`yyyy-MM-dd`) | `2026-05-19` |
| `{PublishedAtUtc}` | Video publish timestamp in UTC (`yyyy-MM-dd HH:mm:ss UTC`) | `2026-05-19 19:18:45 UTC` |
| `{PublishedAtDiscord}` | Discord formatted timestamp (`<t:unix:f>`) | `<t:1779227925:f>` |
| `{PublishedAtDiscordRelative}` | Discord relative timestamp (`<t:unix:R>`) | `<t:1779227925:R>` |
| `{VideoDescription}` | YouTube video description text | `Patch notes and improvements...` |
| `{RoleMention}` | Mention text for configured monitor role, or empty when unset | `<@&1234567890>` |

Notes:

* Placeholder names are case-insensitive.
* Unknown placeholders are left as-is.
* If a template is empty, the monitor falls back to: `[{ChannelName}] {VideoTitle}`.
* If `{RoleMention}` is not used in `DefaultPostBodyTemplate`, the role mention is automatically prepended on its own line (when a role is configured), matching current behavior.
* Escaped newlines in templates are supported (`\\n`, `\\r\\n`, `\\r`) and converted to real line breaks at runtime. This is useful for one-line environment/secret values.

Examples:

* `[{ChannelName}] {VideoTitle}`
* `{PublishedAtDiscordRelative} | {VideoTitle}`
* `New upload from {ChannelName}: {VideoTitle} ({VideoUrl})`

Body template examples:

* `New video from **{ChannelName}**\n{VideoUrl}`
* `{RoleMention}\n**{VideoTitle}**\n{VideoUrl}\nPublished: {PublishedAtDiscord} ({PublishedAtDiscordRelative})`
* `Channel: {ChannelName} ({ChannelId})\n{VideoTitle}\n{VideoDescription}`

### Single-Message Channels

Register channels that should allow only one message per user. Channels must be listed here before `/singlemessage enable` will accept them.

```json
{
  "SingleMessage": {
    "Channels": [
      { "ChannelId": 1234567890123456789, "ScanHistoryOnEnable": false }
    ]
  }
}
```

| Setting | Description |
| --- | --- |
| `ChannelId` | Discord channel ID to register for single-message enforcement |
| `ScanHistoryOnEnable` | When `true`, scans the last 100 messages on enable to pre-populate existing posters (default: `false`) |

Note: `SingleMessage:Channels` is an array and is best managed in `appsettings.json` rather than environment variables.

### Moderation Action Logging

All moderation actions are logged as rich embeds to a Discord forum channel. Each embed shows the action type, the target user, the moderator, and the reason.

> **Note:** `ModerationLog` is a root-level config section, not nested under `Bot`.

| Setting | Description |
| --- | --- |
| `ForumChannelId` | Forum channel ID where moderation log threads are created (`0` = disabled) |
| `ModeratorRoleId` | Optional role to mention in log posts (`0` = no mention) |

### Cross-Channel Spam Detection

Detects users who send identical messages across multiple channels within a short time window. When triggered, a spam alert is posted to the moderation log forum channel with **Ban** and **Dismiss** buttons for moderators. Requires the **Message Content** privileged intent (see [Required Bot Permissions](#required-bot-permissions)).

> **Note:** `CrossChannelSpam` is a root-level config section, not nested under `Bot`.

| Setting | Description |
| --- | --- |
| `Enabled` | Enable cross-channel spam detection (default: `false`) |
| `TimeWindowSeconds` | Sliding window duration in seconds (default: `30`) |
| `MinimumChannelCount` | Minimum number of distinct channels before a detection fires (default: `3`) |
| `DeleteMessages` | Delete detected spam messages (requires Manage Messages). Default: `true` |
| `TimeoutOnDetection` | Apply a 28-day timeout to the spammer (requires Moderate Members). Default: `true` |

### Uptime Heartbeat

Set `Heartbeat:Enabled` to `true` and configure:

| Setting | Description |
| --- | --- |
| `PushUrl` | Uptime Kuma push monitor URL (for example `/api/push/<token>`) |
| `IntervalSeconds` | Heartbeat cadence in seconds (minimum enforced: 15) |
| `StartupDelaySeconds` | Delay after bot startup before first heartbeat |
| `TimeoutSeconds` | HTTP timeout for heartbeat push |

### Status Command

Use the slash command:

* `/status` to post the current status overview publicly in-channel
* `/status private:true` to return the same overview as an ephemeral response only visible to you

### Cooldowns

Cooldowns are configured per command under `Bot:Cooldowns` (in seconds).
Code-level defaults are still used when no config override is provided.

Example:

```json
{
  "Bot": {
    "Cooldowns": {
      "UserInfo": 5,
      "Status": 15
    }
  }
}
```

### Environment Variables

In production, settings are provided via environment variables using the `HUDUCOMMUNITYBOT_` prefix and `__` as the section separator:

```text
HUDUCOMMUNITYBOT_Bot__Token=your-token-here
HUDUCOMMUNITYBOT_Bot__GuildId=1234567890
HUDUCOMMUNITYBOT_Bot__Cooldowns__UserInfo=5
HUDUCOMMUNITYBOT_Bot__Cooldowns__Status=15
HUDUCOMMUNITYBOT_Bot__StatusMonitor__Enabled=true
HUDUCOMMUNITYBOT_Bot__StatusMonitor__ChannelId=1234567890
HUDUCOMMUNITYBOT_Bot__StatusMonitor__RoleId=1234567890
HUDUCOMMUNITYBOT_Bot__HuduReleaseMonitor__Enabled=true
HUDUCOMMUNITYBOT_Bot__HuduReleaseMonitor__ChannelId=1234567890
HUDUCOMMUNITYBOT_Bot__HuduReleaseMonitor__RoleId=1234567890
HUDUCOMMUNITYBOT_Bot__HuduReleaseMonitor__FeedUrl=https://hq.hudu.com/public/releases.json
HUDUCOMMUNITYBOT_Bot__HuduReleaseMonitor__PollIntervalMinutes=15
HUDUCOMMUNITYBOT_Bot__HuduReleaseMonitor__BaselineReleaseId=67
HUDUCOMMUNITYBOT_Bot__HuduCommunityFeedMonitor__Enabled=true
HUDUCOMMUNITYBOT_Bot__HuduCommunityFeedMonitor__ChannelId=1234567890
HUDUCOMMUNITYBOT_Bot__HuduCommunityFeedMonitor__RoleId=1234567890
HUDUCOMMUNITYBOT_Bot__HuduCommunityFeedMonitor__FeedUrl=https://community.hudu.com/rss/feed
HUDUCOMMUNITYBOT_Bot__HuduCommunityFeedMonitor__PollIntervalMinutes=15
HUDUCOMMUNITYBOT_Bot__YoutubeMonitor__Enabled=true
HUDUCOMMUNITYBOT_Bot__YoutubeMonitor__ForumChannelId=1234567890
HUDUCOMMUNITYBOT_Bot__YoutubeMonitor__RoleId=1234567890
HUDUCOMMUNITYBOT_Bot__YoutubeMonitor__YouTubeDataApiKey=your-youtube-data-api-key
HUDUCOMMUNITYBOT_Bot__YoutubeMonitor__PollIntervalMinutes=15
HUDUCOMMUNITYBOT_Bot__YoutubeMonitor__DefaultPostTitleTemplate=[{ChannelName}] {VideoTitle}
HUDUCOMMUNITYBOT_Bot__YoutubeMonitor__DefaultPostBodyTemplate=New video from **{ChannelName}**\n{VideoUrl}
HUDUCOMMUNITYBOT_Bot__Heartbeat__Enabled=true
HUDUCOMMUNITYBOT_Bot__Heartbeat__PushUrl=https://kuma.example.com/api/push/xxxxx
HUDUCOMMUNITYBOT_Bot__Heartbeat__IntervalSeconds=60
HUDUCOMMUNITYBOT_Bot__Heartbeat__StartupDelaySeconds=15
HUDUCOMMUNITYBOT_Bot__Heartbeat__TimeoutSeconds=10
HUDUCOMMUNITYBOT_ModerationLog__ForumChannelId=1234567890
HUDUCOMMUNITYBOT_ModerationLog__ModeratorRoleId=1234567890
HUDUCOMMUNITYBOT_CrossChannelSpam__Enabled=false
HUDUCOMMUNITYBOT_CrossChannelSpam__TimeWindowSeconds=30
HUDUCOMMUNITYBOT_CrossChannelSpam__MinimumChannelCount=3
HUDUCOMMUNITYBOT_CrossChannelSpam__DeleteMessages=true
HUDUCOMMUNITYBOT_CrossChannelSpam__TimeoutOnDetection=true
```

#### Moderation Exemptions Configuration

```bash
# Linux/Mac
export HUDUCOMMUNITYBOT_ModerationExemptions__ExemptUserIds__0=1234567890
export HUDUCOMMUNITYBOT_ModerationExemptions__ExemptRoleIds__0=1234567890

# Windows PowerShell
$env:HUDUCOMMUNITYBOT_ModerationExemptions__ExemptUserIds__0="1234567890"
$env:HUDUCOMMUNITYBOT_ModerationExemptions__ExemptRoleIds__0="1234567890"

# Windows CMD
set HUDUCOMMUNITYBOT_ModerationExemptions__ExemptUserIds__0=1234567890
set HUDUCOMMUNITYBOT_ModerationExemptions__ExemptRoleIds__0=1234567890
```

#### Command Access Configuration

```bash
# Disable all fun commands (meme, 8ball, roll, joke, say)
HUDUCOMMUNITYBOT_CommandAccess__DisableAllFunCommands=true

# Disable specific commands globally
HUDUCOMMUNITYBOT_CommandAccess__DisabledCommands__0=about
HUDUCOMMUNITYBOT_CommandAccess__DisabledCommands__1=avatar
HUDUCOMMUNITYBOT_CommandAccess__DisabledCommands__2=help
HUDUCOMMUNITYBOT_CommandAccess__DisabledCommands__3=ping
HUDUCOMMUNITYBOT_CommandAccess__DisabledCommands__4=remind
HUDUCOMMUNITYBOT_CommandAccess__DisabledCommands__5=serverinfo
HUDUCOMMUNITYBOT_CommandAccess__DisabledCommands__6=userinfo

# Restrict commands to allowed channel IDs
HUDUCOMMUNITYBOT_CommandAccess__RestrictedChannels__about__0=1234567890
HUDUCOMMUNITYBOT_CommandAccess__RestrictedChannels__help__0=1234567890
HUDUCOMMUNITYBOT_CommandAccess__RestrictedChannels__help__1=2345678901
```

### GitHub Secrets (Deploy Workflow)

If you deploy with `.github/workflows/deploy.yml`, configure these repository secrets and they will be written into the runtime `.env` file on host:

| GitHub Secret | Runtime Environment Variable |
| --- | --- |
| `DEPLOY_SSH_KEY` | Used by GitHub Actions SSH setup to connect to the host |
| `DEPLOY_HOST` | Used by GitHub Actions SSH/rsync/scp target host |
| `DISCORD_TOKEN` | `HUDUCOMMUNITYBOT_Bot__Token` |
| `GUILD_ID` | `HUDUCOMMUNITYBOT_Bot__GuildId` |
| `STATUS_MONITOR_ENABLED` | `HUDUCOMMUNITYBOT_Bot__StatusMonitor__Enabled` |
| `STATUS_MONITOR_CHANNEL_ID` | `HUDUCOMMUNITYBOT_Bot__StatusMonitor__ChannelId` |
| `STATUS_MONITOR_ROLE_ID` | `HUDUCOMMUNITYBOT_Bot__StatusMonitor__RoleId` |
| `HUDU_RELEASE_MONITOR_ENABLED` | `HUDUCOMMUNITYBOT_Bot__HuduReleaseMonitor__Enabled` |
| `HUDU_RELEASE_MONITOR_CHANNEL_ID` | `HUDUCOMMUNITYBOT_Bot__HuduReleaseMonitor__ChannelId` |
| `HUDU_RELEASE_MONITOR_ROLE_ID` | `HUDUCOMMUNITYBOT_Bot__HuduReleaseMonitor__RoleId` |
| `HUDU_RELEASE_MONITOR_FEED_URL` | `HUDUCOMMUNITYBOT_Bot__HuduReleaseMonitor__FeedUrl` |
| `HUDU_RELEASE_MONITOR_POLL_INTERVAL_MINUTES` | `HUDUCOMMUNITYBOT_Bot__HuduReleaseMonitor__PollIntervalMinutes` |
| `HUDU_RELEASE_MONITOR_BASELINE_RELEASE_ID` | `HUDUCOMMUNITYBOT_Bot__HuduReleaseMonitor__BaselineReleaseId` |
| `HUDU_COMMUNITY_FEED_MONITOR_ENABLED` | `HUDUCOMMUNITYBOT_Bot__HuduCommunityFeedMonitor__Enabled` |
| `HUDU_COMMUNITY_FEED_MONITOR_CHANNEL_ID` | `HUDUCOMMUNITYBOT_Bot__HuduCommunityFeedMonitor__ChannelId` |
| `HUDU_COMMUNITY_FEED_MONITOR_ROLE_ID` | `HUDUCOMMUNITYBOT_Bot__HuduCommunityFeedMonitor__RoleId` |
| `HUDU_COMMUNITY_FEED_MONITOR_FEED_URL` | `HUDUCOMMUNITYBOT_Bot__HuduCommunityFeedMonitor__FeedUrl` |
| `HUDU_COMMUNITY_FEED_MONITOR_POLL_INTERVAL_MINUTES` | `HUDUCOMMUNITYBOT_Bot__HuduCommunityFeedMonitor__PollIntervalMinutes` |
| `YOUTUBE_MONITOR_ENABLED` | `HUDUCOMMUNITYBOT_Bot__YoutubeMonitor__Enabled` |
| `YOUTUBE_FORUM_CHANNEL_ID` | `HUDUCOMMUNITYBOT_Bot__YoutubeMonitor__ForumChannelId` |
| `YOUTUBE_MONITOR_ROLE_ID` | `HUDUCOMMUNITYBOT_Bot__YoutubeMonitor__RoleId` |
| `YOUTUBE_DATA_API_KEY` | `HUDUCOMMUNITYBOT_Bot__YoutubeMonitor__YouTubeDataApiKey` |
| `YOUTUBE_POLL_INTERVAL_MINUTES` | `HUDUCOMMUNITYBOT_Bot__YoutubeMonitor__PollIntervalMinutes` |
| `YOUTUBE_DEFAULT_POST_TITLE_TEMPLATE` | `HUDUCOMMUNITYBOT_Bot__YoutubeMonitor__DefaultPostTitleTemplate` |
| `YOUTUBE_DEFAULT_POST_BODY_TEMPLATE` | `HUDUCOMMUNITYBOT_Bot__YoutubeMonitor__DefaultPostBodyTemplate` |
| `HEARTBEAT_ENABLED` | `HUDUCOMMUNITYBOT_Bot__Heartbeat__Enabled` |
| `HEARTBEAT_PUSH_URL` | `HUDUCOMMUNITYBOT_Bot__Heartbeat__PushUrl` |
| `HEARTBEAT_INTERVAL_SECONDS` | `HUDUCOMMUNITYBOT_Bot__Heartbeat__IntervalSeconds` |
| `HEARTBEAT_STARTUP_DELAY_SECONDS` | `HUDUCOMMUNITYBOT_Bot__Heartbeat__StartupDelaySeconds` |
| `HEARTBEAT_TIMEOUT_SECONDS` | `HUDUCOMMUNITYBOT_Bot__Heartbeat__TimeoutSeconds` |
| `MODERATION_LOG_FORUM_CHANNEL_ID` | `HUDUCOMMUNITYBOT_ModerationLog__ForumChannelId` |
| `MODERATION_LOG_MODERATOR_ROLE_ID` | `HUDUCOMMUNITYBOT_ModerationLog__ModeratorRoleId` |
| `MODERATION_LOG_EVENT_AUDIT_ENABLED` | `HUDUCOMMUNITYBOT_ModerationLog__EventAuditEnabled` |
| `MODERATION_LOG_EVENT_AUDIT_CHANNEL_ID` | `HUDUCOMMUNITYBOT_ModerationLog__EventAuditChannelId` |
| `MODERATION_LOG_EVENT_AUDIT_LOG_MESSAGE_DELETES` | `HUDUCOMMUNITYBOT_ModerationLog__LogMessageDeletes` |
| `MODERATION_LOG_EVENT_AUDIT_LOG_MEMBER_LEAVES` | `HUDUCOMMUNITYBOT_ModerationLog__LogMemberLeaves` |
| `MODERATION_LOG_AUDIT_LOG_LOOKBACK_SECONDS` | `HUDUCOMMUNITYBOT_ModerationLog__AuditLogLookbackSeconds` |
| `CROSS_CHANNEL_SPAM_ENABLED` | `HUDUCOMMUNITYBOT_CrossChannelSpam__Enabled` |
| `CROSS_CHANNEL_SPAM_TIME_WINDOW_SECONDS` | `HUDUCOMMUNITYBOT_CrossChannelSpam__TimeWindowSeconds` |
| `CROSS_CHANNEL_SPAM_MINIMUM_CHANNEL_COUNT` | `HUDUCOMMUNITYBOT_CrossChannelSpam__MinimumChannelCount` |
| `CROSS_CHANNEL_SPAM_DELETE_MESSAGES` | `HUDUCOMMUNITYBOT_CrossChannelSpam__DeleteMessages` |
| `CROSS_CHANNEL_SPAM_TIMEOUT_ON_DETECTION` | `HUDUCOMMUNITYBOT_CrossChannelSpam__TimeoutOnDetection` |
| `MODERATION_EXEMPT_USER_ID_0` | `HUDUCOMMUNITYBOT_ModerationExemptions__ExemptUserIds__0` |
| `MODERATION_EXEMPT_ROLE_ID_0` | `HUDUCOMMUNITYBOT_ModerationExemptions__ExemptRoleIds__0` |
| `COMMAND_ACCESS_DISABLE_ALL_FUN_COMMANDS` | `HUDUCOMMUNITYBOT_CommandAccess__DisableAllFunCommands` |
| `COMMAND_ACCESS_DISABLED_COMMAND_0` | `HUDUCOMMUNITYBOT_CommandAccess__DisabledCommands__0` |
| `COMMAND_ACCESS_DISABLED_COMMAND_1` | `HUDUCOMMUNITYBOT_CommandAccess__DisabledCommands__1` |
| `COMMAND_ACCESS_DISABLED_COMMAND_2` | `HUDUCOMMUNITYBOT_CommandAccess__DisabledCommands__2` |
| `COMMAND_ACCESS_DISABLED_COMMAND_3` | `HUDUCOMMUNITYBOT_CommandAccess__DisabledCommands__3` |
| `COMMAND_ACCESS_DISABLED_COMMAND_4` | `HUDUCOMMUNITYBOT_CommandAccess__DisabledCommands__4` |
| `COMMAND_ACCESS_DISABLED_COMMAND_5` | `HUDUCOMMUNITYBOT_CommandAccess__DisabledCommands__5` |
| `COMMAND_ACCESS_DISABLED_COMMAND_6` | `HUDUCOMMUNITYBOT_CommandAccess__DisabledCommands__6` |

Note: `YoutubeMonitor:Channels` is best managed through `/youtube add` and persisted in SQLite, instead of storing an array in secrets.

## 🚢 Deployment

See [`.github/deployment/DEPLOYMENT_SETUP.md`](.github/deployment/DEPLOYMENT_SETUP.md) for full host setup instructions, including:

* Creating the `deployer` service account
* Installing the systemd service unit
* Configuring the `.env` file
* Setting up the required sudoers entries for the GitHub Actions deploy workflow

Deployments are triggered automatically by the `deploy.yml` workflow after a successful CI build on `main`, or manually via `workflow_dispatch`.

## 🏷️ Versioning

HuduCommunityBot now uses a `VersionManager` tool to keep `src/HuduCommunityBot/HuduCommunityBot.csproj` and `CHANGELOG.md` in sync.

1. Build the tool:

```bash
dotnet build tools/VersionManager/VersionManager.csproj -c Release
```

1. Optional commit analysis:

```bash
dotnet artifacts/bin/VersionManager/release/VersionManager.dll check-commits
```

1. Bump version:

```bash
dotnet artifacts/bin/VersionManager/release/VersionManager.dll bump --version X.Y.Z --type patch --message "Your description"
```

1. Validate consistency:

```bash
dotnet artifacts/bin/VersionManager/release/VersionManager.dll validate
```

There is also a helper script:

```bash
./Bump-Version.ps1 -Version X.Y.Z -Type patch -Message "Your description"
```

## 🔧 Tech Stack

* [.NET 10](https://dotnet.microsoft.com/) / C# 13
* [Discord.Net 3.x](https://github.com/discord-net/Discord.Net)
* `Microsoft.Extensions.Hosting` / `IHostedService`
* Central package management via `Directory.Packages.props`
