using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Tests.Fixtures;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Domain.ValueObjects;
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

    private async Task ClearWorldChangesAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
        await db.WorldChanges.ExecuteDeleteAsync();
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
    public async Task Issues_LocationsWithoutGps_UsesInheritedParentGps()
    {
        var game = await CreateGameAsync();

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
            var parent = new Location
            {
                Name = "Parent coords",
                LocationKind = LocationKind.Wilderness,
                Coordinates = new GpsCoordinates(49.4532m, 18.0203m)
            };
            db.Locations.Add(parent);
            await db.SaveChangesAsync();

            var child = new Location
            {
                Name = "Child inherits coords",
                LocationKind = LocationKind.Wilderness,
                ParentLocationId = parent.Id
            };
            db.Locations.Add(child);
            await db.SaveChangesAsync();

            db.GameLocations.Add(new GameLocation { GameId = game.Id, LocationId = child.Id });
            await db.SaveChangesAsync();
        }

        var issues = await Client.GetFromJsonAsync<DashboardIssuesDto>(
            $"/api/dashboard/issues?gameId={game.Id}");

        Assert.NotNull(issues);
        var noGpsIssue = Assert.Single(issues.Issues, i => i.Key == "locations-no-gps");
        Assert.Equal(0, noGpsIssue.Count);
    }

    [Fact]
    public async Task Issues_AllSix_AlwaysReturned()
    {
        // Even when a fresh game has zero issues across the board, the
        // endpoint returns all six issue rows so the UI keeps a stable
        // shape. The 'quests-no-encounters' and 'monsters-no-loot' rows
        // were dropped by design — quest encounters and monster loot are
        // optional, so flagging "missing" was noise. Severity for all
        // remaining rows should be "low" (count == 0).
        var game = await CreateGameAsync();

        var issues = await Client.GetFromJsonAsync<DashboardIssuesDto>(
            $"/api/dashboard/issues?gameId={game.Id}");

        Assert.NotNull(issues);
        Assert.Equal(6, issues.Issues.Count);
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
        await ClearWorldChangesAsync();

        var rows = await Client.GetFromJsonAsync<List<DashboardActivityDto>>(
            $"/api/dashboard/activity?gameId={game.Id}");

        Assert.NotNull(rows);
        Assert.Empty(rows);
    }

    [Fact]
    public async Task Activity_WorldChanges_ReturnsNewestScopedRows()
    {
        var game = await CreateGameAsync();
        var otherGame = await CreateGameAsync();
        await ClearWorldChangesAsync();

        var now = DateTime.UtcNow;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
            db.WorldChanges.AddRange(
                new WorldChange
                {
                    GameId = game.Id,
                    EntityType = nameof(Location),
                    EntityId = 10,
                    EntityName = "Starý brod",
                    Operation = WorldChangeOperation.Updated,
                    ActorUserId = "test-user",
                    ActorDisplayName = "Test Organizátor",
                    ChangedAtUtc = now
                },
                new WorldChange
                {
                    GameId = null,
                    EntityType = nameof(Item),
                    EntityId = 20,
                    EntityName = "Hojivý lektvar",
                    Operation = WorldChangeOperation.Created,
                    ActorUserId = "system",
                    ActorDisplayName = "System",
                    ChangedAtUtc = now.AddMinutes(1)
                },
                new WorldChange
                {
                    GameId = otherGame.Id,
                    EntityType = nameof(Npc),
                    EntityId = 30,
                    EntityName = "Cizí NPC",
                    Operation = WorldChangeOperation.Deleted,
                    ActorUserId = "other",
                    ActorDisplayName = "Other",
                    ChangedAtUtc = now.AddMinutes(2)
                });
            await db.SaveChangesAsync();
        }

        var rows = await Client.GetFromJsonAsync<List<DashboardActivityDto>>(
            $"/api/dashboard/activity?gameId={game.Id}");

        Assert.NotNull(rows);
        Assert.Collection(rows,
            row =>
            {
                Assert.Equal("item", row.EntityType);
                Assert.Equal(20, row.EntityId);
                Assert.Equal("Hojivý lektvar", row.EntityName);
                Assert.Equal("System", row.ActorName);
                Assert.Equal("vytvořil", row.Verb);
                Assert.Null(row.ThumbnailUrl);
            },
            row =>
            {
                Assert.Equal("location", row.EntityType);
                Assert.Equal(10, row.EntityId);
                Assert.Equal("Starý brod", row.EntityName);
                Assert.Equal("Test Organizátor", row.ActorName);
                Assert.Equal("upravil", row.Verb);
            });
    }

    // /aktivity full-page log — uncapped paged feed of raw WorldChange rows
    // for the active game (plus catalog-wide where GameId is null), DESC by
    // ChangedAtUtc, no merge/projection so the client can DxGrid-filter the
    // entire roll. Cross-game rows must NOT leak.
    [Fact]
    public async Task WorldChange_ReturnsRawRowsScopedToGameAndCatalog()
    {
        var game = await CreateGameAsync();
        var otherGame = await CreateGameAsync();
        await ClearWorldChangesAsync();

        var now = DateTime.UtcNow;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
            db.WorldChanges.AddRange(
                new WorldChange
                {
                    GameId = game.Id,
                    EntityType = nameof(Location),
                    EntityId = 10,
                    EntityName = "Starý brod",
                    Operation = WorldChangeOperation.Updated,
                    ActorUserId = "u1",
                    ActorDisplayName = "Drozd",
                    ChangedAtUtc = now
                },
                new WorldChange
                {
                    GameId = null,
                    EntityType = nameof(Item),
                    EntityId = 20,
                    EntityName = "Hojivý lektvar",
                    Operation = WorldChangeOperation.Created,
                    ActorUserId = "system",
                    ActorDisplayName = "System",
                    ChangedAtUtc = now.AddMinutes(1)
                },
                new WorldChange
                {
                    GameId = otherGame.Id,
                    EntityType = nameof(Npc),
                    EntityId = 30,
                    EntityName = "Cizí NPC",
                    Operation = WorldChangeOperation.Deleted,
                    ActorUserId = "other",
                    ActorDisplayName = "Other",
                    ChangedAtUtc = now.AddMinutes(2)
                });
            await db.SaveChangesAsync();
        }

        var rows = await Client.GetFromJsonAsync<List<WorldChangeRowDto>>(
            $"/api/dashboard/world-change?gameId={game.Id}");

        Assert.NotNull(rows);
        Assert.Equal(2, rows.Count);

        // Newest first, catalog-wide row (GameId == null) precedes the game-scoped one.
        Assert.Equal(nameof(Item), rows[0].EntityType);
        Assert.Null(rows[0].GameId);
        Assert.Equal(WorldChangeOperation.Created, rows[0].Operation);
        Assert.Equal("System", rows[0].ActorDisplayName);

        Assert.Equal(nameof(Location), rows[1].EntityType);
        Assert.Equal(game.Id, rows[1].GameId);
        Assert.Equal(WorldChangeOperation.Updated, rows[1].Operation);
        Assert.Equal("Drozd", rows[1].ActorDisplayName);
        Assert.Equal("Starý brod", rows[1].EntityName);
    }

    [Fact]
    public async Task WorldChange_NonExistentGame_Returns404()
    {
        // §1 — REST 404 before 200-with-empty so orchestrator typos
        // surface instead of being masked.
        var response = await Client.GetAsync("/api/dashboard/world-change?gameId=999999");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task WorldChange_TakeParameter_CapsResultCount()
    {
        var game = await CreateGameAsync();
        await ClearWorldChangesAsync();

        var now = DateTime.UtcNow;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
            for (var i = 0; i < 5; i++)
            {
                db.WorldChanges.Add(new WorldChange
                {
                    GameId = game.Id,
                    EntityType = nameof(Location),
                    EntityId = 100 + i,
                    EntityName = $"L{i}",
                    Operation = WorldChangeOperation.Updated,
                    ActorUserId = "u1",
                    ActorDisplayName = "Drozd",
                    ChangedAtUtc = now.AddMinutes(i)
                });
            }
            await db.SaveChangesAsync();
        }

        var rows = await Client.GetFromJsonAsync<List<WorldChangeRowDto>>(
            $"/api/dashboard/world-change?gameId={game.Id}&take=2");

        Assert.NotNull(rows);
        Assert.Equal(2, rows.Count);
        // Newest 2 in DESC order — i=4 then i=3.
        Assert.Equal("L4", rows[0].EntityName);
        Assert.Equal("L3", rows[1].EntityName);
    }

    [Fact]
    public async Task WorldChangeInterceptor_ApiCreateGame_RecordsAuthenticatedActor()
    {
        var game = await CreateGameAsync();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
        var row = await db.WorldChanges.SingleAsync(c =>
            c.EntityType == nameof(Game) && c.EntityId == game.Id);

        Assert.Equal(game.Id, row.GameId);
        Assert.Equal("Cockpit", row.EntityName);
        Assert.Equal(WorldChangeOperation.Created, row.Operation);
        Assert.Equal("test-user", row.ActorUserId);
        Assert.Equal("Test Organizátor", row.ActorDisplayName);
    }

    [Fact]
    public async Task WorldChangeInterceptor_BackgroundSave_UsesSystemActor()
    {
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
            db.Locations.Add(new Location
            {
                Name = "Bez kontextu",
                LocationKind = LocationKind.Wilderness
            });
            await db.SaveChangesAsync();
        }

        using var assertScope = Factory.Services.CreateScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<WorldDbContext>();
        var row = await assertDb.WorldChanges.SingleAsync(c => c.EntityType == nameof(Location));

        Assert.Equal(WorldChangeOperation.Created, row.Operation);
        Assert.Equal("system", row.ActorUserId);
        Assert.Equal("System", row.ActorDisplayName);
    }

    [Fact]
    public async Task WorldChangeInterceptor_ExplicitTransaction_SkipsAuditWithoutBlockingUserData()
    {
        await ClearWorldChangesAsync();

        int locationId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync();
            var location = new Location
            {
                Name = "Transakční paseka",
                LocationKind = LocationKind.Wilderness
            };
            db.Locations.Add(location);
            await db.SaveChangesAsync();
            locationId = location.Id;
            await tx.CommitAsync();
        }

        using var assertScope = Factory.Services.CreateScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<WorldDbContext>();
        Assert.True(await assertDb.Locations.AnyAsync(l => l.Id == locationId));
        Assert.False(await assertDb.WorldChanges.AnyAsync(c =>
            c.EntityType == nameof(Location) && c.EntityId == locationId));
    }

    // Issue #478 — raw WorldActivity table on the cockpit. Returns audit
    // rows 1:1 (no merge with legacy entity timestamps), ordered DESC,
    // includes Location.Name when present.
    [Fact]
    public async Task WorldActivity_ReturnsRawRowsOrderedDesc()
    {
        var game = await CreateGameAsync();
        int locationId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
            var location = new Location
            {
                Name = "Aradhrynd",
                LocationKind = LocationKind.Town
            };
            db.Locations.Add(location);
            await db.SaveChangesAsync();
            db.GameLocations.Add(new GameLocation { GameId = game.Id, LocationId = location.Id });
            db.WorldActivities.AddRange(
                new WorldActivity
                {
                    GameId = game.Id,
                    TimestampUtc = DateTime.UtcNow.AddMinutes(-10),
                    OrganizerUserId = "u1",
                    OrganizerName = "Drozd",
                    ActivityType = WorldActivityType.LocationPlaced,
                    Description = $"Umístěna lokace: {location.Name}",
                    LocationId = location.Id,
                    DataJson = "{}"
                },
                new WorldActivity
                {
                    GameId = game.Id,
                    TimestampUtc = DateTime.UtcNow.AddMinutes(-1),
                    OrganizerUserId = "u2",
                    OrganizerName = "Tasha",
                    ActivityType = WorldActivityType.CharacterLevelUp,
                    Description = "Beorn dosáhl 3. úrovně",
                    DataJson = "{\"level\":3}"
                });
            await db.SaveChangesAsync();
            locationId = location.Id;
        }

        var rows = await Client.GetFromJsonAsync<List<WorldActivityRowDto>>(
            $"/api/dashboard/world-activity?gameId={game.Id}");

        Assert.NotNull(rows);
        Assert.Equal(2, rows.Count);

        // Newest first.
        Assert.Equal(WorldActivityType.CharacterLevelUp, rows[0].ActivityType);
        Assert.Equal("Tasha", rows[0].OrganizerName);
        Assert.Null(rows[0].LocationId);
        Assert.Null(rows[0].LocationName);

        Assert.Equal(WorldActivityType.LocationPlaced, rows[1].ActivityType);
        Assert.Equal("Drozd", rows[1].OrganizerName);
        Assert.Equal(locationId, rows[1].LocationId);
        Assert.Equal("Aradhrynd", rows[1].LocationName);
    }

    [Fact]
    public async Task WorldActivity_NonExistentGame_Returns404()
    {
        // §1 — REST 404 before 200-with-empty so orchestrator bugs
        // (typo'd gameId, stale URL) surface instead of being masked.
        var response = await Client.GetAsync("/api/dashboard/world-activity?gameId=999999");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task WorldActivity_OtherGame_IsExcluded()
    {
        var gameA = await CreateGameAsync();
        var gameB = await CreateGameAsync();
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
            db.WorldActivities.Add(new WorldActivity
            {
                GameId = gameB.Id,
                TimestampUtc = DateTime.UtcNow,
                OrganizerUserId = "u1",
                OrganizerName = "Other",
                ActivityType = WorldActivityType.QuestCompleted,
                Description = "Quest done in other game",
                DataJson = "{}"
            });
            await db.SaveChangesAsync();
        }

        var rows = await Client.GetFromJsonAsync<List<WorldActivityRowDto>>(
            $"/api/dashboard/world-activity?gameId={gameA.Id}");

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

    [Fact]
    public async Task RecentEvents_ReturnsCharacterEventsScopedByGameAndSince()
    {
        var game = await CreateGameAsync();
        var otherGame = await CreateGameAsync();
        var cutoff = DateTime.UtcNow.AddMinutes(-10);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
            var character = new Character { Name = "Mireček", IsPlayedCharacter = true };
            var otherCharacter = new Character { Name = "Cizí", IsPlayedCharacter = true };
            db.Characters.AddRange(character, otherCharacter);
            await db.SaveChangesAsync();

            var assignment = new CharacterAssignment
            {
                CharacterId = character.Id,
                GameId = game.Id,
                ExternalPersonId = 3001
            };
            var otherAssignment = new CharacterAssignment
            {
                CharacterId = otherCharacter.Id,
                GameId = otherGame.Id,
                ExternalPersonId = 3002
            };
            db.CharacterAssignments.AddRange(assignment, otherAssignment);
            await db.SaveChangesAsync();

            db.CharacterEvents.AddRange(
                new CharacterEvent
                {
                    CharacterAssignmentId = assignment.Id,
                    Timestamp = cutoff.AddMinutes(-1),
                    OrganizerUserId = "old",
                    OrganizerName = "Old",
                    EventType = CharacterEventType.Note,
                    Data = "{}"
                },
                new CharacterEvent
                {
                    CharacterAssignmentId = assignment.Id,
                    Timestamp = cutoff.AddMinutes(1),
                    OrganizerUserId = "tom",
                    OrganizerName = "Tomáš",
                    EventType = CharacterEventType.LevelUp,
                    Data = """{"level":2}"""
                },
                new CharacterEvent
                {
                    CharacterAssignmentId = otherAssignment.Id,
                    Timestamp = cutoff.AddMinutes(2),
                    OrganizerUserId = "other",
                    OrganizerName = "Other",
                    EventType = CharacterEventType.LevelUp,
                    Data = """{"level":9}"""
                });
            await db.SaveChangesAsync();
        }

        var rows = await Client.GetFromJsonAsync<List<DashboardRecentEventDto>>(
            $"/api/dashboard/events/recent?gameId={game.Id}&since={Uri.EscapeDataString(cutoff.ToString("O"))}");

        Assert.NotNull(rows);
        var row = Assert.Single(rows);
        Assert.Equal("Mireček", row.CharacterName);
        Assert.Equal(CharacterEventType.LevelUp, row.EventType);
        Assert.Equal("Tomáš", row.OrganizerName);
    }

    [Fact]
    public async Task RecentEvents_CapsAt50NewestRows()
    {
        var game = await CreateGameAsync();

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
            var character = new Character { Name = "Blesk", IsPlayedCharacter = true };
            db.Characters.Add(character);
            await db.SaveChangesAsync();

            var assignment = new CharacterAssignment
            {
                CharacterId = character.Id,
                GameId = game.Id,
                ExternalPersonId = 3003
            };
            db.CharacterAssignments.Add(assignment);
            await db.SaveChangesAsync();

            var start = DateTime.UtcNow.AddHours(-1);
            for (var i = 0; i < 55; i++)
            {
                db.CharacterEvents.Add(new CharacterEvent
                {
                    CharacterAssignmentId = assignment.Id,
                    Timestamp = start.AddMinutes(i),
                    OrganizerUserId = "tom",
                    OrganizerName = "Tomáš",
                    EventType = CharacterEventType.Note,
                    Data = $$"""{"index":{{i}}}"""
                });
            }
            await db.SaveChangesAsync();
        }

        var rows = await Client.GetFromJsonAsync<List<DashboardRecentEventDto>>(
            $"/api/dashboard/events/recent?gameId={game.Id}");

        Assert.NotNull(rows);
        Assert.Equal(50, rows.Count);
        Assert.Contains("\"index\":54", rows[0].Data);
        Assert.DoesNotContain(rows, r => r.Data.Contains("\"index\":0", StringComparison.Ordinal));
    }
}
