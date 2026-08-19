using DiscordBot.Models;
using DiscordBot.Services;
using Xunit;

namespace HuduCommunityBot.Tests;

public sealed class HuduReleaseMonitorServiceTests
{
    [Fact]
    public void ResolveReleaseDisplayUrl_PrefersCommunityPostUrlWhenAvailable()
    {
        var url = HuduReleaseMonitorService.ResolveReleaseDisplayUrlForTests(
            "https://hq.hudu.com/releases/123.json",
            "https://community.hudu.com/release-notes/hudu-version-2-44-0");

        Assert.Equal("https://community.hudu.com/release-notes/hudu-version-2-44-0", url);
    }

    [Fact]
    public void ResolveReleaseDisplayUrl_FallsBackToReleaseUrlWhenCommunityPostIsMissing()
    {
        var url = HuduReleaseMonitorService.ResolveReleaseDisplayUrlForTests(
            "https://hq.hudu.com/releases/123.json",
            null);

        Assert.Equal("https://hq.hudu.com/releases/123.json", url);
    }

    [Fact]
    public void FindCommunityPostLink_MatchesCommunityReleaseTitleWithExtraContext()
    {
        var link = HuduReleaseMonitorService.FindCommunityPostLinkForTests(
            "2.44.1",
            [
                ("Hudu Version 2.44.1 - Release Notes", "https://community.hudu.com/release-notes/hudu-version-2-44-1")
            ]);

        Assert.Equal("https://community.hudu.com/release-notes/hudu-version-2-44-1", link);
    }

    [Fact]
    public void ParseCommunityItems_UsesAtomEntryLinkHrefWhenPresent()
    {
        var items = HuduReleaseMonitorService.ParseCommunityItemsForTests(
            """
            <feed xmlns="http://www.w3.org/2005/Atom">
              <entry>
                <title>Hudu Version 2.44.1 - Release Notes</title>
                <link href="https://www.reddit.com/r/hudu/comments/abc123/hudu_version_2441/" />
              </entry>
            </feed>
            """);

        Assert.Single(items);
        Assert.Equal("https://www.reddit.com/r/hudu/comments/abc123/hudu_version_2441/", items[0].Link);
        Assert.Equal("Hudu Version 2.44.1 - Release Notes", items[0].Title);
    }

    [Fact]
    public void FindMatchingDiscussionPost_UsesCachedFeedItems()
    {
        var match = HuduReleaseMonitorService.FindMatchingDiscussionPostForTests(
            "2.44.1",
            [
                new HuduCommunityPostMatch(
                    "Hudu Version 2.44.1 - Release Notes",
                    "https://www.reddit.com/r/hudu/comments/abc123/hudu_version_2441/")
            ]);

        Assert.NotNull(match);
        Assert.Equal("https://www.reddit.com/r/hudu/comments/abc123/hudu_version_2441/", match!.Link);
    }

    [Fact]
    public void FindMatchingDiscussionPost_MatchesRedditLiveAnnouncementTitles()
    {
        var match = HuduReleaseMonitorService.FindMatchingDiscussionPostForTests(
            "2.44.2",
            [
                new HuduCommunityPostMatch(
                    "V2.44.2 is Live!",
                    "https://www.reddit.com/r/hudu/comments/1vh6dys/v2442_is_live/")
            ]);

        Assert.NotNull(match);
        Assert.Equal("https://www.reddit.com/r/hudu/comments/1vh6dys/v2442_is_live/", match!.Link);
    }

    [Fact]
    public void ResolveDiscussionFeedUrl_PrefersReleaseMonitorDiscussionFeedUrl()
    {
        var config = new BotConfig
        {
            HuduReleaseMonitor = new HuduReleaseMonitorConfig
            {
                DiscussionFeedUrl = "https://www.reddit.com/r/hudu/.rss"
            },
            HuduCommunityFeedMonitor = new HuduCommunityFeedMonitorConfig
            {
                FeedUrl = "https://community.hudu.com/rss/feed"
            }
        };

        var feedUrl = HuduReleaseMonitorService.ResolveDiscussionFeedUrlForTests(config);

        Assert.Equal("https://www.reddit.com/r/hudu/.rss", feedUrl);
    }

    [Fact]
    public void ShouldRetroactivelyUpdateExistingReleasePost_RecognizesCommunityLinkAsWrong()
    {
        var shouldUpdate = HuduReleaseMonitorService.ShouldRetroactivelyUpdateExistingReleasePostForTests(
            "https://community.hudu.com/release-notes/hudu-version-2-44-0",
            "https://www.reddit.com/r/hudu/comments/abc123/hudu_version_2440/");

        Assert.True(shouldUpdate);
    }

    [Fact]
    public void HasMatchingCommunityAnnouncement_MatchesWhenMessageIncludesRoleMention()
    {
        var isMatch = HuduReleaseMonitorService.HasMatchingCommunityAnnouncementForTests(
            "<@&12345> Community discussion thread for Release 2.44.2: https://www.reddit.com/r/hudu/comments/1vh6dys/v2442_is_live/",
            "2.44.2",
            "https://www.reddit.com/r/hudu/comments/1vh6dys/v2442_is_live/");

        Assert.True(isMatch);
    }

    [Fact]
    public void HasMatchingCommunityAnnouncement_MatchesIgnoringTrailingSlash()
    {
        var isMatch = HuduReleaseMonitorService.HasMatchingCommunityAnnouncementForTests(
            "Community discussion thread for Release 2.44.2: https://www.reddit.com/r/hudu/comments/1vh6dys/v2442_is_live",
            "2.44.2",
            "https://www.reddit.com/r/hudu/comments/1vh6dys/v2442_is_live/");

        Assert.True(isMatch);
    }

    [Fact]
    public void HasMatchingCommunityAnnouncement_DoesNotMatchDifferentReleaseVersion()
    {
        var isMatch = HuduReleaseMonitorService.HasMatchingCommunityAnnouncementForTests(
            "Community discussion thread for Release 2.44.1: https://www.reddit.com/r/hudu/comments/1vh6dys/v2442_is_live/",
            "2.44.2",
            "https://www.reddit.com/r/hudu/comments/1vh6dys/v2442_is_live/");

        Assert.False(isMatch);
    }

    [Fact]
    public void ResolveLastPostedReleaseId_UsesBaselineWhenStateIsMissing()
    {
        var lastPostedReleaseId = HuduReleaseMonitorService.ResolveLastPostedReleaseIdForTests(null, 67);

        Assert.Equal(67, lastPostedReleaseId);
    }

    [Fact]
    public void GetRetrospectiveUpdateReleases_ReturnsKnownRecentReleasesWhenNoNewReleasesExist()
    {
        var releaseIds = HuduReleaseMonitorService.GetRetrospectiveUpdateReleaseIdsForTests(
            [1, 3, 4, 5],
            baselineReleaseId: 3,
            lastPostedReleaseId: 5);

        Assert.Equal([3, 4, 5], releaseIds);
    }
}
