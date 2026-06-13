using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DiscordBot.Core.Data;

/// <summary>
/// Design-time factory for EF Core migrations tooling.
/// </summary>
public class HuduCommunityBotContextFactory : IDesignTimeDbContextFactory<HuduCommunityBotContext>
{
    public HuduCommunityBotContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<HuduCommunityBotContext>();
        optionsBuilder.UseSqlite("Data Source=./huducommunitybot.db");
        return new HuduCommunityBotContext(optionsBuilder.Options);
    }
}
