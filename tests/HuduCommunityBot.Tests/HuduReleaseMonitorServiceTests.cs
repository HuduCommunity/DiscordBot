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
}
