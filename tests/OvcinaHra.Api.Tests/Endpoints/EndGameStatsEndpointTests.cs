using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Tests.Fixtures;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Tests.Endpoints;

public class EndGameStatsEndpointTests(PostgresFixture postgres)
    : IntegrationTestBase(postgres), IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task EndGameStats_NonExistentGame_Returns404()
    {
        var response = await Client.GetAsync("/api/games/999999/end-game-stats");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task EndGameStats_ReturnsKingdomLevelsAndActivityStats()
    {
        // Arrange
        var game = await CreateGameAsync();
        Kingdom esgaroth;
        Kingdom aradhryand;
        Kingdom azanulinbar;
        Kingdom arnor;

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
            esgaroth = await GetKingdomAsync(db, "Esgaroth");
            aradhryand = await GetKingdomAsync(db, "Aradhryand");
            azanulinbar = await GetKingdomAsync(db, "Azanulinbar-Dum");
            arnor = await GetKingdomAsync(db, "Nový Arnor");

            var hilda = new Character { Name = $"Hilda {game.Id}", IsPlayedCharacter = true };
            var beorn = new Character { Name = $"Beorn {game.Id}", IsPlayedCharacter = true };
            var mira = new Character { Name = $"Míra {game.Id}", IsPlayedCharacter = true };
            db.Characters.AddRange(hilda, beorn, mira);
            await db.SaveChangesAsync();

            var hildaAssignment = new CharacterAssignment
            {
                CharacterId = hilda.Id,
                GameId = game.Id,
                ExternalPersonId = game.Id * 1000 + 1,
                Class = PlayerClass.Warrior,
                KingdomId = esgaroth.Id
            };
            var beornAssignment = new CharacterAssignment
            {
                CharacterId = beorn.Id,
                GameId = game.Id,
                ExternalPersonId = game.Id * 1000 + 2,
                KingdomId = esgaroth.Id
            };
            var miraAssignment = new CharacterAssignment
            {
                CharacterId = mira.Id,
                GameId = game.Id,
                ExternalPersonId = game.Id * 1000 + 3,
                KingdomId = aradhryand.Id
            };
            db.CharacterAssignments.AddRange(hildaAssignment, beornAssignment, miraAssignment);
            await db.SaveChangesAsync();

            var start = DateTime.UtcNow.AddHours(-2);
            db.WorldActivities.AddRange(
                LevelUp(game.Id, hildaAssignment.Id, start.AddMinutes(1), """{"level":2}"""),
                LevelUp(game.Id, hildaAssignment.Id, start.AddMinutes(2), """{"level":5}"""),
                LevelUp(game.Id, beornAssignment.Id, start.AddMinutes(3), """{"level":3}"""),
                LevelUp(game.Id, miraAssignment.Id, start.AddMinutes(4), """{"level":2}"""),
                new WorldActivity
                {
                    GameId = game.Id,
                    TimestampUtc = start.AddMinutes(5),
                    OrganizerUserId = "org",
                    OrganizerName = "Org",
                    ActivityType = WorldActivityType.MonsterDefeated,
                    Description = "Hilda přemohla nestvůru: Skřet",
                    CharacterAssignmentId = hildaAssignment.Id,
                    DataJson = """{"monsterName":"Skřet"}"""
                },
                new WorldActivity
                {
                    GameId = game.Id,
                    TimestampUtc = start.AddMinutes(6),
                    OrganizerUserId = "org",
                    OrganizerName = "Org",
                    ActivityType = WorldActivityType.MonsterDefeated,
                    Description = "Beorn přemohl nestvůru: Skřet",
                    CharacterAssignmentId = beornAssignment.Id,
                    DataJson = """{"monsterName":"Skřet"}"""
                },
                new WorldActivity
                {
                    GameId = game.Id,
                    TimestampUtc = start.AddMinutes(7),
                    OrganizerUserId = "org",
                    OrganizerName = "Org",
                    ActivityType = WorldActivityType.MonsterDefeated,
                    Description = "Míra přemohl nestvůru: Vlk",
                    CharacterAssignmentId = miraAssignment.Id,
                    DataJson = "{}"
                },
                new WorldActivity
                {
                    GameId = game.Id,
                    TimestampUtc = start.AddMinutes(8),
                    OrganizerUserId = "org",
                    OrganizerName = "Org",
                    ActivityType = WorldActivityType.HeroFell,
                    Description = "Míra padl pod nestvůrou: Vlk",
                    CharacterAssignmentId = miraAssignment.Id,
                    DataJson = """{"monsterName":"Vlk"}"""
                });
            db.CharacterEvents.Add(Mastery(hildaAssignment.Id, start.AddMinutes(9), "Berserk"));
            await db.SaveChangesAsync();
        }

        // Act
        var stats = await Client.GetFromJsonAsync<EndGameStatsDto>(
            $"/api/games/{game.Id}/end-game-stats");

        // Assert
        Assert.NotNull(stats);
        Assert.Equal("Esgaroth", stats.FirstToLevel5!.KingdomName);
        Assert.StartsWith("Hilda", stats.FirstToLevel5.HeroName, StringComparison.Ordinal);
        Assert.Equal(esgaroth.HexColor, stats.FirstToLevel5.ColorHex);

        Assert.Contains(stats.Kingdoms, k => k.KingdomName == aradhryand.Name);
        Assert.Contains(stats.Kingdoms, k => k.KingdomName == azanulinbar.Name);
        Assert.Contains(stats.Kingdoms, k => k.KingdomName == esgaroth.Name);
        Assert.Contains(stats.Kingdoms, k => k.KingdomName == arnor.Name);
        Assert.All(stats.Kingdoms, AssertCompleteLevelGrid);

        var esgarothStats = stats.Kingdoms.Single(k => k.KingdomName == "Esgaroth");
        Assert.Equal(esgaroth.HexColor, esgarothStats.ColorHex);
        Assert.Equal(2, esgarothStats.HeroCount);
        Assert.Equal(7, esgarothStats.TotalLevelsGained);
        Assert.Equal(1, esgarothStats.Levels.Single(l => l.Level == 3).HeroCount);
        Assert.Equal(0, esgarothStats.Levels.Single(l => l.Level == 5).HeroCount);
        Assert.Equal(1, esgarothStats.Levels.Single(l => l.Level == 6).HeroCount);

        var azanulinbarStats = stats.Kingdoms.Single(k => k.KingdomName == azanulinbar.Name);
        Assert.Equal(azanulinbar.HexColor, azanulinbarStats.ColorHex);
        Assert.Equal(0, azanulinbarStats.HeroCount);
        Assert.All(azanulinbarStats.Levels, level => Assert.Equal(0, level.HeroCount));

        Assert.Equal("Esgaroth", stats.ExperienceLeaderboard[0].KingdomName);
        Assert.Equal(7, stats.ExperienceLeaderboard[0].TotalLevelsGained);

        Assert.Equal(2, stats.MonsterStats.MostDefeatedMonsters.Length);
        var topMonster = stats.MonsterStats.MostDefeatedMonsters[0];
        Assert.Equal("Skřet", topMonster.MonsterName);
        Assert.Equal(2, topMonster.Count);
        Assert.Contains(stats.MonsterStats.MostDefeatedMonsters,
            m => m.MonsterName == "Vlk" && m.Count == 1);

        var fallen = Assert.Single(stats.MonsterStats.HeroesFallenByKingdom);
        Assert.Equal("Aradhryand", fallen.KingdomName);
        Assert.Equal(aradhryand.HexColor, fallen.ColorHex);
        Assert.Equal(1, fallen.Count);
    }

    [Fact]
    public async Task EndGameStats_ReportsMasteryLevelsSixAndSevenAndClampsExtraMasteries()
    {
        // Arrange
        var game = await CreateGameAsync();
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
            var esgaroth = await GetKingdomAsync(db, "Esgaroth");

            var oneMastery = new Character { Name = $"One Mastery {game.Id}", IsPlayedCharacter = true };
            var twoMasteries = new Character { Name = $"Two Masteries {game.Id}", IsPlayedCharacter = true };
            var corruptMasteries = new Character { Name = $"Corrupt Masteries {game.Id}", IsPlayedCharacter = true };
            db.Characters.AddRange(oneMastery, twoMasteries, corruptMasteries);
            await db.SaveChangesAsync();

            var oneAssignment = Assignment(game, oneMastery, esgaroth, 11);
            var twoAssignment = Assignment(game, twoMasteries, esgaroth, 12);
            var corruptAssignment = Assignment(game, corruptMasteries, esgaroth, 13);
            db.CharacterAssignments.AddRange(oneAssignment, twoAssignment, corruptAssignment);
            await db.SaveChangesAsync();

            var start = DateTime.UtcNow.AddHours(-1);
            db.WorldActivities.AddRange(
                LevelUp(game.Id, oneAssignment.Id, start.AddMinutes(1), """{"level":5}"""),
                LevelUp(game.Id, twoAssignment.Id, start.AddMinutes(2), """{"level":5}"""),
                LevelUp(game.Id, corruptAssignment.Id, start.AddMinutes(3), """{"level":5}"""));
            db.CharacterEvents.AddRange(
                Mastery(oneAssignment.Id, start.AddMinutes(4), "Berserk"),
                Mastery(twoAssignment.Id, start.AddMinutes(5), "Berserk"),
                Mastery(twoAssignment.Id, start.AddMinutes(6), "Houževnatý"),
                Mastery(corruptAssignment.Id, start.AddMinutes(7), "Berserk"),
                Mastery(corruptAssignment.Id, start.AddMinutes(8), "Houževnatý"),
                Mastery(corruptAssignment.Id, start.AddMinutes(9), "Navíc"));
            await db.SaveChangesAsync();
        }

        // Act
        var stats = await Client.GetFromJsonAsync<EndGameStatsDto>(
            $"/api/games/{game.Id}/end-game-stats");

        // Assert
        Assert.NotNull(stats);
        var esgarothStats = stats.Kingdoms.Single(k => k.KingdomName == "Esgaroth");
        AssertCompleteLevelGrid(esgarothStats);
        Assert.Equal("(6)", esgarothStats.Levels.Single(l => l.Level == 6).Label);
        Assert.Equal("(7)", esgarothStats.Levels.Single(l => l.Level == 7).Label);
        Assert.Equal(0, esgarothStats.Levels.Single(l => l.Level == 5).HeroCount);
        Assert.Equal(1, esgarothStats.Levels.Single(l => l.Level == 6).HeroCount);
        Assert.Equal(2, esgarothStats.Levels.Single(l => l.Level == 7).HeroCount);
    }

    private async Task<GameDetailDto> CreateGameAsync()
    {
        var response = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("End game stats", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GameDetailDto>())!;
    }

    private static Task<Kingdom> GetKingdomAsync(WorldDbContext db, string name) =>
        db.Kingdoms.SingleAsync(k => k.Name == name);

    private static CharacterAssignment Assignment(
        GameDetailDto game,
        Character character,
        Kingdom kingdom,
        int personOffset) => new()
        {
            CharacterId = character.Id,
            GameId = game.Id,
            ExternalPersonId = game.Id * 1000 + personOffset,
            Class = PlayerClass.Warrior,
            KingdomId = kingdom.Id
        };

    private static WorldActivity LevelUp(
        int gameId,
        int assignmentId,
        DateTime timestampUtc,
        string dataJson) => new()
    {
        GameId = gameId,
        TimestampUtc = timestampUtc,
        OrganizerUserId = "org",
        OrganizerName = "Org",
        ActivityType = WorldActivityType.CharacterLevelUp,
        Description = "Hrdina dosáhl nové úrovně",
        CharacterAssignmentId = assignmentId,
        DataJson = dataJson
    };

    private static CharacterEvent Mastery(
        int assignmentId,
        DateTime timestampUtc,
        string skillName) => new()
    {
        CharacterAssignmentId = assignmentId,
        Timestamp = timestampUtc,
        OrganizerUserId = "org",
        OrganizerName = "Org",
        EventType = CharacterEventType.SkillGained,
        Data = JsonSerializer.Serialize(new { skill = skillName, mastery = true })
    };

    private static void AssertCompleteLevelGrid(KingdomLevelBreakdownDto kingdom)
    {
        Assert.Equal(7, kingdom.Levels.Length);
        Assert.Equal(Enumerable.Range(1, 7), kingdom.Levels.Select(l => l.Level));
        Assert.Equal(["1", "2", "3", "4", "5", "(6)", "(7)"], kingdom.Levels.Select(l => l.Label));
    }
}
