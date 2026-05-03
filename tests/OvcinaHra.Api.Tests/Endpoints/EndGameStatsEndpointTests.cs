using System.Net;
using System.Net.Http.Json;
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
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
            var esgaroth = await GetOrCreateKingdomAsync(db, "Esgaroth", "#000000", 1);
            var aradhryand = await GetOrCreateKingdomAsync(db, "Aradhryand", "#000000", 2);

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
                    ActivityType = WorldActivityType.HeroFell,
                    Description = "Míra padl pod nestvůrou: Vlk",
                    CharacterAssignmentId = miraAssignment.Id,
                    DataJson = """{"monsterName":"Vlk"}"""
                });
            await db.SaveChangesAsync();
        }

        // Act
        var stats = await Client.GetFromJsonAsync<EndGameStatsDto>(
            $"/api/games/{game.Id}/end-game-stats");

        // Assert
        Assert.NotNull(stats);
        Assert.Equal("Esgaroth", stats.FirstToLevel5!.KingdomName);
        Assert.StartsWith("Hilda", stats.FirstToLevel5.HeroName, StringComparison.Ordinal);
        Assert.Equal("#242F3D", stats.FirstToLevel5.ColorHex);

        var esgarothStats = stats.Kingdoms.Single(k => k.KingdomName == "Esgaroth");
        Assert.Equal("#242F3D", esgarothStats.ColorHex);
        Assert.Equal(2, esgarothStats.HeroCount);
        Assert.Equal(6, esgarothStats.TotalLevelsGained);
        Assert.Equal(1, esgarothStats.Levels.Single(l => l.Level == 3).HeroCount);
        Assert.Equal(1, esgarothStats.Levels.Single(l => l.Level == 5).HeroCount);

        Assert.Equal("Esgaroth", stats.ExperienceLeaderboard[0].KingdomName);
        Assert.Equal(6, stats.ExperienceLeaderboard[0].TotalLevelsGained);

        var topMonster = Assert.Single(stats.MonsterStats.MostDefeatedMonsters);
        Assert.Equal("Skřet", topMonster.MonsterName);
        Assert.Equal(2, topMonster.Count);

        var fallen = Assert.Single(stats.MonsterStats.HeroesFallenByKingdom);
        Assert.Equal("Aradhryand", fallen.KingdomName);
        Assert.Equal("#243525", fallen.ColorHex);
        Assert.Equal(1, fallen.Count);
    }

    private async Task<GameDetailDto> CreateGameAsync()
    {
        var response = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("End game stats", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GameDetailDto>())!;
    }

    private static async Task<Kingdom> GetOrCreateKingdomAsync(
        WorldDbContext db,
        string name,
        string hexColor,
        int sortOrder)
    {
        var kingdom = await db.Kingdoms.FirstOrDefaultAsync(k => k.Name == name);
        if (kingdom is not null) return kingdom;

        kingdom = new Kingdom { Name = name, HexColor = hexColor, SortOrder = sortOrder };
        db.Kingdoms.Add(kingdom);
        await db.SaveChangesAsync();
        return kingdom;
    }

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
}
