using DiscordBot.Models;
using Xunit;

namespace HuduCommunityBot.Tests;

public sealed class GitHubMonitorConfigTests
{
    [Fact]
    public void Validate_AllowsDisabledMonitorWithoutRepositories()
    {
        var config = new GitHubMonitorConfig();

        config.Validate();
    }

    [Fact]
    public void Validate_RejectsNonPositivePollIntervalWhenEnabled()
    {
        var config = new GitHubMonitorConfig
        {
            Enabled = true,
            PollIntervalMinutes = 0
        };

        Assert.Throws<InvalidOperationException>(config.Validate);
    }

    [Fact]
    public void Validate_RejectsRepositoryMissingOwnerOrNameWhenEnabled()
    {
        var config = new GitHubMonitorConfig
        {
            Enabled = true,
            Repositories = [new GitHubRepositoryConfig { Owner = "example" }]
        };

        Assert.Throws<InvalidOperationException>(config.Validate);
    }

    [Fact]
    public void TrackedRepository_MapsEachCategorySettingAndChannel()
    {
        var repository = new GitHubTrackedRepository
        {
            IssuesEnabled = false,
            IssuesChannelId = 101,
            PullRequestsEnabled = true,
            PullRequestsChannelId = 102,
            ActionsEnabled = true,
            ActionsChannelId = 103,
            ReleasesEnabled = false,
            ReleasesChannelId = 104
        };

        Assert.False(repository.IsCategoryEnabled(GitHubEventCategory.Issues));
        Assert.Equal(101UL, repository.GetCategoryChannelId(GitHubEventCategory.Issues));
        Assert.True(repository.IsCategoryEnabled(GitHubEventCategory.PullRequests));
        Assert.Equal(102UL, repository.GetCategoryChannelId(GitHubEventCategory.PullRequests));
        Assert.True(repository.IsCategoryEnabled(GitHubEventCategory.Actions));
        Assert.Equal(103UL, repository.GetCategoryChannelId(GitHubEventCategory.Actions));
        Assert.False(repository.IsCategoryEnabled(GitHubEventCategory.Releases));
        Assert.Equal(104UL, repository.GetCategoryChannelId(GitHubEventCategory.Releases));
    }
}
