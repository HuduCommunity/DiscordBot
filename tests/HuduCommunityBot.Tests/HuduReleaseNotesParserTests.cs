using DiscordBot.Services;
using Xunit;

namespace HuduCommunityBot.Tests;

public sealed class HuduReleaseNotesParserTests
{
    [Fact]
    public void Parse_DotZeroRelease_ExtractsAndOrdersCanonicalSections()
    {
        const string notes = """
            <div class=\"trix-content\">
              <div>Hudu v2.43.0 is now live.</div>
              <div><strong>New Features</strong></div>
              <ul><li>Feature A</li></ul>
              <div><strong>Improvements</strong></div>
              <ul><li>Improvement A</li></ul>
              <div><strong>Bug Fixes</strong></div>
              <ul><li>Fix A</li></ul>
            </div>
            """;

        var parsed = HuduReleaseMonitorService.ParseReleaseNotesForTests("2.43.0", notes);

        Assert.Contains("Hudu v2.43.0 is now live.", parsed.IntroText, StringComparison.Ordinal);
        Assert.Equal(["New Features", "Improvements", "Bug Fixes"], parsed.Sections.Select(s => s.Title).ToArray());
        Assert.Equal("Feature A", parsed.Sections[0].Items.Single());
        Assert.Equal("Improvement A", parsed.Sections[1].Items.Single());
        Assert.Equal("Fix A", parsed.Sections[2].Items.Single());
    }

    [Fact]
    public void Parse_PatchRelease_NormalizesFixedAndChangedHeadings()
    {
        const string notes = """
            <div class=\"trix-content\">
              <div><strong>Fixed</strong></div>
              <ul><li>Fixed thing</li></ul>
              <div><strong>Changed</strong></div>
              <ul><li>Changed thing</li></ul>
            </div>
            """;

        var parsed = HuduReleaseMonitorService.ParseReleaseNotesForTests("2.41.2", notes);

        Assert.Equal(["Improvements", "Bug Fixes"], parsed.Sections.Select(s => s.Title).ToArray());
        Assert.Equal("Changed thing", parsed.Sections[0].Items.Single());
        Assert.Equal("Fixed thing", parsed.Sections[1].Items.Single());
    }

    [Fact]
    public void Parse_PatchRelease_HandlesColonSectionHeadings()
    {
        const string notes = """
            <div class=\"trix-content\">
              <div>Patch intro text.</div>
              <div><strong>Improvements:</strong></div>
              <ul><li>Improvement 1</li><li>Improvement 2</li></ul>
              <div><strong>Bug Fixes:</strong></div>
              <ul><li>Fix 1</li></ul>
            </div>
            """;

        var parsed = HuduReleaseMonitorService.ParseReleaseNotesForTests("2.41.1", notes);

        Assert.Contains("Patch intro text.", parsed.IntroText, StringComparison.Ordinal);
        Assert.Equal(["Improvements", "Bug Fixes"], parsed.Sections.Select(s => s.Title).ToArray());
        Assert.Equal(["Improvement 1", "Improvement 2"], parsed.Sections[0].Items.ToArray());
        Assert.Equal(["Fix 1"], parsed.Sections[1].Items.ToArray());
    }

    [Fact]
    public void Parse_PatchRelease_PreservesUnknownSectionsAfterKnownOnes()
    {
        const string notes = """
            <div class=\"trix-content\">
              <div><strong>Notes for Release</strong></div>
              <ul><li>Special instruction</li></ul>
              <div><strong>Bug Fixes</strong></div>
              <ul><li>Fix 1</li></ul>
            </div>
            """;

        var parsed = HuduReleaseMonitorService.ParseReleaseNotesForTests("2.40.2", notes);

        Assert.Equal(["Bug Fixes", "Notes for Release"], parsed.Sections.Select(s => s.Title).ToArray());
    }
}
