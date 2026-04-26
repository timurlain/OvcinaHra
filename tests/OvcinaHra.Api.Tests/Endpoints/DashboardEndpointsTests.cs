using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Tests.Fixtures;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Tests.Endpoints;

// Issue #158 — contract tests for the four /api/dashboard endpoints
// that power the Home cockpit. Stats counts, Pozor issue flagging,
// activity stub ordering, timeline upcoming-only filter.
public class DashboardEndpointsTests(PostgresFixture postgres)
    : IntegrationTestBase(postgres), IClassFixture<PostgresFixture>
{
    private async Task<GameDetailDto> CreateGameAsync()
    {
        var response = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Cockpit", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GameDetailDto>())!;
    }

    [Fact]
    public async Task Stats_EmptyGame_ReturnsAllZeros()
    {
        var game = await CreateGameAsync();

        var stats = await Client.GetFromJsonAsync<DashboardStatsDto>(
            $"/api/dashboard/stats?gameId={game.Id}");

        Assert.NotNull(stats);
        Assert.Equal(0, stats.LocationsCount);
        Assert.Equal(0, stats.CharactersCount);
        Assert.Equal(0, stats.ItemsCount);
        Assert.Equal(0, stats.SpellsCount);
        Assert.Equal(0, stats.SkillsCount);
        Assert.Equal(0, stats.TreasuresCount);
        Assert.Equal(0, stats.QuestsCount);
    }

    [Fact]
    public async Task Stats_SeededGame_ReturnsCounts()
    {
        var game = await CreateGameAsync();

        // Seed two locations linked to the game; only the GameLocation rows
        // count, so it doesn't matter that other games might share the catalog.
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
            var loc1 = new Location { Name = "L1", LocationKind = LocationKind.Wilderness };
            var loc2 = new Location { Name = "L2", LocationKind = LocationKind.Wilderness };
            db.Locations.AddRange(loc1, loc2);
            await db.SaveChangesAsync();
            db.GameLocations.AddRange(
                new GameLocation { GameId = game.Id, LocationId = loc1.Id },
                new GameLocation { GameId = game.Id, LocationId = loc2.Id });
            await db.SaveChangesAsync();
        }

        var stats = await Client.GetFromJsonAsync<DashboardStatsDto>(
            $"/api/dashboard/stats?gameId={game.Id}");

        Assert.NotNull(stats);
        Assert.Equal(2, stats.LocationsCount);
    }

    [Fact]
    public async Task Issues_LocationsWithoutGps_AreFlagged()
    {
        var game = await CreateGameAsync();

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
            var noGps = new Location { Name = "No coords", LocationKind = LocationKind.Wilderness };
            db.Locations.Add(noGps);
            await db.SaveChangesAsync();
            db.GameLocations.Add(new GameLocation { GameId = game.Id, LocationId = noGps.Id });
            await db.SaveChangesAsync();
        }

        var issues = await Client.GetFromJsonAsync<DashboardIssuesDto>(
            $"/api/dashboard/issues?gameId={game.Id}");

        Assert.NotNull(issues);
        var noGpsIssue = Assert.Single(issues.Issues, i => i.Key == "locations-no-gps");
        Assert.Equal(1, noGpsIssue.Count);
        Assert.Equal("/locations", noGpsIssue.TargetRoute);
    }

    [Fact]
    public async Task Issues_AllEight_AlwaysReturned()
    {
        // Even when a fresh game has zero issues across the board, the
        // endpoint returns all eight issue rows so the UI keeps a stable
        // shape. Severity for all should be "low" (count == 0).
        var game = await CreateGameAsync();

        var issues = await Client.GetFromJsonAsync<DashboardIssuesDto>(
            $"/api/dashboard/issues?gameId={game.Id}");

        Assert.NotNull(issues);
        Assert.Equal(8, issues.Issues.Count);
        Assert.All(issues.Issues, i => Assert.Equal("low", i.Severity));
    }

    [Fact]
    public async Task Issues_SkillsWithoutEffect_ScopedToGame()
    {
        // Two games. Each gets a GameSkill with empty Effect. The dashboard
        // for game A must report 1 skill-without-effect, not 2 (regression
        // guard for the catalog-wide vs game-scoped Copilot finding).
        var gameA = await CreateGameAsync();
        var gameB = await CreateGameAsync();

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
            db.Skills.Add(new Skill { Name = "Catalog skill", Category = SkillCategory.Class });
            await db.SaveChangesAsync();
            db.GameSkills.AddRange(
                new GameSkill { GameId = gameA.Id, Name = "A-skill", Category = SkillCategory.Class, Effect = null },
                new GameSkill { GameId = gameB.Id, Name = "B-skill", Category = SkillCategory.Class, Effect = null });
            await db.SaveChangesAsync();
        }

        var issuesA = await Client.GetFromJsonAsync<DashboardIssuesDto>(
            $"/api/dashboard/issues?gameId={gameA.Id}");
        var skillsA = issuesA!.Issues.Single(i => i.Key == "skills-no-effect");
        Assert.Equal(1, skillsA.Count);
    }

    [Fact]
    public async Task Activity_NoEdits_ReturnsEmpty()
    {
        var game = await CreateGameAsync();

        var rows = await Client.GetFromJsonAsync<List<DashboardActivityDto>>(
            $"/api/dashboard/activity?gameId={game.Id}");

        Assert.NotNull(rows);
        Assert.Empty(rows);
    }

    [Fact]
    public async Task Timeline_PastSlot_IsExcluded()
    {
        var game = await CreateGameAsync();

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
            // Past slot: should be excluded.
            db.GameTimeSlots.Add(new GameTimeSlot
            {
                GameId = game.Id,
                StartTime = DateTime.UtcNow.AddHours(-3),
                Duration = TimeSpan.FromHours(1)
            });
            // Upcoming slot: should be returned.
            db.GameTimeSlots.Add(new GameTimeSlot
            {
                GameId = game.Id,
                StartTime = DateTime.UtcNow.AddHours(2),
                Duration = TimeSpan.FromHours(1)
            });
            await db.SaveChangesAsync();
        }

        var rows = await Client.GetFromJsonAsync<List<TimelineRowDto>>(
            $"/api/dashboard/timeline?gameId={game.Id}");

        Assert.NotNull(rows);
        Assert.Single(rows);
        Assert.True(rows[0].StartTime > DateTime.UtcNow);
    }

    [Fact]
    public async Task Timeline_RunningSlot_StatusIsProbiha()
    {
        var game = await CreateGameAsync();

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
            // Started 30 min ago, lasts 2 hours → currently running.
            db.GameTimeSlots.Add(new GameTimeSlot
            {
                GameId = game.Id,
                StartTime = DateTime.UtcNow.AddMinutes(-30),
                Duration = TimeSpan.FromHours(2)
            });
            await db.SaveChangesAsync();
        }

        var rows = await Client.GetFromJsonAsync<List<TimelineRowDto>>(
            $"/api/dashboard/timeline?gameId={game.Id}");

        Assert.NotNull(rows);
        var row = Assert.Single(rows);
        Assert.True(row.IsRunning);
    }
}
