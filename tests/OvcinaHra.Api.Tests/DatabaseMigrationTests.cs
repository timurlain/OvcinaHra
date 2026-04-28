using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Tests.Fixtures;

namespace OvcinaHra.Api.Tests;

public class DatabaseMigrationTests(PostgresFixture postgres) : IntegrationTestBase(postgres), IClassFixture<PostgresFixture>
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

    [Fact]
    public async Task PromoteQuestTimeSlotToFk_Migration_BackfillsExactMatchesAndDownRestoresLabels()
    {
        var dbName = $"ovcina_migration_{Guid.NewGuid():N}";
        var adminConnection = new NpgsqlConnectionStringBuilder(Postgres.ConnectionString)
        {
            Database = "postgres",
            Pooling = false
        }.ConnectionString;
        var scratchConnection = new NpgsqlConnectionStringBuilder(Postgres.ConnectionString)
        {
            Database = dbName,
            Pooling = false
        }.ConnectionString;

        await ExecuteNonQueryAsync(adminConnection, $"""CREATE DATABASE "{dbName}";""");

        try
        {
            var options = new DbContextOptionsBuilder<WorldDbContext>()
                .UseNpgsql(scratchConnection)
                .Options;

            await using var setup = new WorldDbContext(options);
            var migrator = setup.Database.GetService<IMigrator>();
            await migrator.MigrateAsync("20260427213427_DropRecipeCategory");

            const string expectedLabel = "Střed hry: Rok 1247, 1.5. 12:00 (2 h)";
            await ExecuteNonQueryAsync(scratchConnection, $$"""
                INSERT INTO "Games" ("Id", "Name", "Edition", "StartDate", "EndDate", "Status")
                VALUES (100, 'Migration Game', 1, '2026-05-01', '2026-05-03', 'Draft');

                INSERT INTO "GameTimeSlots" ("Id", "GameId", "StartTime", "Duration", "Stage", "InGameYear")
                VALUES (200, 100, '2026-05-01 10:00:00+00', interval '2 hours', 'Midgame', 1247);

                INSERT INTO "Quests" ("Id", "Name", "QuestType", "GameId", "State", "TimeSlot")
                VALUES
                    (300, 'Matched', 'Timed', 100, 'Inactive', '{{expectedLabel}}'),
                    (301, 'Unmatched', 'Timed', 100, 'Inactive', 'sobota dopoledne'),
                    (302, 'Catalog', 'Timed', NULL, 'Inactive', '{{expectedLabel}}');
                """);

            await migrator.MigrateAsync();

            Assert.Equal(200, await ExecuteScalarAsync<int?>(
                scratchConnection, """SELECT "TimeSlotId" FROM "Quests" WHERE "Id" = 300;"""));
            Assert.Null(await ExecuteScalarAsync<int?>(
                scratchConnection, """SELECT "TimeSlotId" FROM "Quests" WHERE "Id" = 301;"""));
            Assert.Null(await ExecuteScalarAsync<int?>(
                scratchConnection, """SELECT "TimeSlotId" FROM "Quests" WHERE "Id" = 302;"""));
            Assert.Equal(0L, await ExecuteScalarAsync<long>(
                scratchConnection,
                """
                SELECT COUNT(*)
                FROM information_schema.columns
                WHERE table_name = 'Quests' AND column_name = 'TimeSlot';
                """));

            await migrator.MigrateAsync("20260427213427_DropRecipeCategory");

            Assert.Equal(expectedLabel, await ExecuteScalarAsync<string>(
                scratchConnection, """SELECT "TimeSlot" FROM "Quests" WHERE "Id" = 300;"""));
            Assert.Equal(0L, await ExecuteScalarAsync<long>(
                scratchConnection,
                """
                SELECT COUNT(*)
                FROM information_schema.columns
                WHERE table_name = 'Quests' AND column_name = 'TimeSlotId';
                """));
        }
        finally
        {
            await ExecuteNonQueryAsync(adminConnection, $"""DROP DATABASE IF EXISTS "{dbName}" WITH (FORCE);""");
        }
    }

    private static async Task ExecuteNonQueryAsync(string connectionString, string sql)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<T?> ExecuteScalarAsync<T>(string connectionString, string sql)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var value = await cmd.ExecuteScalarAsync();
        return value is null or DBNull ? default : (T)value;
    }
}
