using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Tests.Fixtures;

namespace OvcinaHra.Api.Tests;

public class DatabaseMigrationTests(PostgresFixture postgres) : IntegrationTestBase(postgres)
{
    [Fact]
    public async Task Database_HasPendingModelChanges_IsFalse()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();

        // Verify migrations are in sync with the model
        var pending = await db.Database.GetPendingMigrationsAsync();
        Assert.Empty(pending);
    }

    [Fact]
    public async Task Database_AllTablesExist()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();

        // Verify we can query each DbSet without error
        Assert.Equal(0, await db.Games.CountAsync());
        Assert.Equal(0, await db.Locations.CountAsync());
        Assert.Equal(0, await db.Items.CountAsync());
        Assert.Equal(0, await db.Monsters.CountAsync());
        Assert.Equal(0, await db.Quests.CountAsync());
        Assert.Equal(0, await db.Tags.CountAsync());
    }
}
