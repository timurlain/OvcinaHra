using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Services;
using OvcinaHra.Api.Tests.Fixtures;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Tests.Endpoints;

public class ProgressionStatsEndpointTests(PostgresFixture postgres)
    : IntegrationTestBase(postgres), IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task ProgressionStats_NonExistentGame_Returns404()
    {
        var json = await Client.GetAsync("/api/games/999999/progression-stats");
        var xlsx = await Client.GetAsync("/api/games/999999/progression-stats.xlsx");

        Assert.Equal(HttpStatusCode.NotFound, json.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, xlsx.StatusCode);
    }

    [Fact]
    public async Task ProgressionStats_ReturnsTimeslotSnapshotsAndEventRows()
    {
        var game = await CreateGameAsync();
        int day1LastSlotId;
        int slot5Id;
        int slot7Id;
        string day1LastSlotShortLabel;
        string day1LastSlotLongLabel;
        string slot5ShortLabel;

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
            var aradhryand = await GetKingdomAsync(db, "Aradhryand");
            var azanulinbar = await GetKingdomAsync(db, "Azanulinbar-Dum");
            var esgaroth = await GetKingdomAsync(db, "Esgaroth");
            var arnor = await GetKingdomAsync(db, "Nový Arnor");

            var start = new DateTime(2026, 5, 1, 8, 0, 0, DateTimeKind.Utc);
            var slots = SeedTimeSlots(game.Id, start);
            db.GameTimeSlots.AddRange(slots);
            await db.SaveChangesAsync();
            day1LastSlotId = slots[2].Id;
            slot5Id = slots[4].Id;
            slot7Id = slots[6].Id;
            day1LastSlotShortLabel = PragueTimeFormatter.Format(slots[2].StartTime, "HH:mm");
            day1LastSlotLongLabel = StatsLabel(slots[2]);
            slot5ShortLabel = PragueTimeFormatter.Format(slots[4].StartTime, "HH:mm");

            var gapHero = Character("Gap Hero", "Gita", "Mezera");
            var finalHero = Character("Final Hero", "Fero", "Finále");
            var afterHero = Character("After Hero", "Anežka", "Dohra");
            var masteryHero = Character("Master Hero", "Marek", "Mistr");
            db.Characters.AddRange(gapHero, finalHero, afterHero, masteryHero);
            await db.SaveChangesAsync();

            var gapAssignment = Assignment(game, gapHero, esgaroth, 1);
            var finalAssignment = Assignment(game, finalHero, aradhryand, 2);
            var afterAssignment = Assignment(game, afterHero, azanulinbar, 3);
            var masteryAssignment = Assignment(game, masteryHero, arnor, 4);
            db.CharacterAssignments.AddRange(gapAssignment, finalAssignment, afterAssignment, masteryAssignment);
            await db.SaveChangesAsync();

            db.CharacterEvents.AddRange(
                LevelUp(gapAssignment.Id, start.AddHours(13), 1),
                LevelUp(gapAssignment.Id, slots[4].StartTime.AddMinutes(5), 2),
                Mastery(gapAssignment.Id, slots[6].StartTime.AddMinutes(10), "Berserk"),
                LevelUp(finalAssignment.Id, slots[7].StartTime.AddMinutes(20), 4),
                LevelUp(afterAssignment.Id, slots[7].StartTime.Add(slots[7].Duration).AddHours(1), 3),
                LevelUp(masteryAssignment.Id, slots[5].StartTime.AddMinutes(5), 5),
                Mastery(masteryAssignment.Id, slots[6].StartTime.AddMinutes(15), "Berserk"),
                Mastery(masteryAssignment.Id, slots[6].StartTime.AddMinutes(16), "Houževnatý"),
                Mastery(masteryAssignment.Id, slots[6].StartTime.AddMinutes(17), "Navíc"),
                SkillEvent(masteryAssignment.Id, slots[6].StartTime.AddMinutes(18), new { skill = "Legacy" }));
            await db.SaveChangesAsync();
        }

        var stats = await Client.GetFromJsonAsync<ProgressionStatsDto>(
            $"/api/games/{game.Id}/progression-stats");

        Assert.NotNull(stats);
        Assert.Equal(4, stats.Kingdoms.Length);
        Assert.All(stats.Kingdoms, kingdom =>
        {
            Assert.Equal(8, kingdom.Buckets.Length);
            Assert.All(kingdom.Buckets, bucket => Assert.Equal(7, bucket.HeroCountByLevel.Length));
        });

        var esgarothStats = stats.Kingdoms.Single(k => k.KingdomName == "Esgaroth");
        var day1LastBucket = esgarothStats.Buckets.Single(b => b.TimeSlotId == day1LastSlotId);
        Assert.Equal(day1LastSlotShortLabel, day1LastBucket.TimeSlotShortLabel);
        Assert.Equal(day1LastSlotLongLabel, day1LastBucket.Label);
        Assert.Equal(1, day1LastBucket.HeroCountByLevel[0]);

        var slot5Bucket = esgarothStats.Buckets.Single(b => b.TimeSlotId == slot5Id);
        Assert.Equal(slot5ShortLabel, slot5Bucket.TimeSlotShortLabel);
        Assert.Equal(1, slot5Bucket.HeroCountByLevel[1]);
        Assert.Equal(1, esgarothStats.Buckets.Single(b => b.TimeSlotId == slot7Id).HeroCountByLevel[5]);

        var aradhryandStats = stats.Kingdoms.Single(k => k.KingdomName == "Aradhryand");
        Assert.Equal(1, aradhryandStats.Buckets.Single(b => b.TimeSlotId == slot7Id).HeroCountByLevel[3]);

        var azanulinbarStats = stats.Kingdoms.Single(k => k.KingdomName == "Azanulinbar-Dum");
        Assert.Equal(1, azanulinbarStats.Buckets.Single(b => b.TimeSlotId == slot7Id).HeroCountByLevel[2]);

        var arnorStats = stats.Kingdoms.Single(k => k.KingdomName == "Nový Arnor");
        Assert.Equal(1, arnorStats.Buckets.Single(b => b.TimeSlotId == slot7Id).HeroCountByLevel[6]);

        Assert.Contains(stats.Events, e =>
            e.CharacterName.StartsWith("Gap Hero", StringComparison.Ordinal)
            && e.TimeSlotId == day1LastSlotId
            && e.TimeSlotShortLabel == day1LastSlotShortLabel
            && e.TimeSlotLabel == day1LastSlotLongLabel
            && e.LevelGained == 1);
        Assert.Contains(stats.Events, e =>
            e.EventType == "MasterySkillGained"
            && e.LevelGained == 6);
        Assert.Contains(stats.Events, e =>
            e.EventType == "MasterySkillGained"
            && e.LevelGained == 7);
        Assert.Equal(8, stats.Events.Length);
        Assert.DoesNotContain(stats.Events, e =>
            e.CharacterName.StartsWith("Master Hero", StringComparison.Ordinal)
            && e.EventType == "MasterySkillGained"
            && e.LevelGained > 7);
    }

    [Fact]
    public async Task ProgressionStatsXlsx_ReturnsWorkbookWithRows()
    {
        var game = await CreateGameAsync();
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
            var esgaroth = await GetKingdomAsync(db, "Esgaroth");
            var start = new DateTime(2026, 5, 1, 8, 0, 0, DateTimeKind.Utc);
            var slots = SeedTimeSlots(game.Id, start);
            db.GameTimeSlots.AddRange(slots);
            var hero = Character("Excel Hero", "Eliška", "Export");
            db.Characters.Add(hero);
            await db.SaveChangesAsync();

            var assignment = Assignment(game, hero, esgaroth, 9);
            db.CharacterAssignments.Add(assignment);
            await db.SaveChangesAsync();
            db.CharacterEvents.Add(LevelUp(assignment.Id, slots[0].StartTime.AddMinutes(5), 1));
            await db.SaveChangesAsync();
        }

        var response = await Client.GetAsync($"/api/games/{game.Id}/progression-stats.xlsx");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            response.Content.Headers.ContentType?.MediaType);
        var contentDisposition = response.Content.Headers.ContentDisposition;
        var fileName = contentDisposition?.FileNameStar ?? contentDisposition?.FileName?.Trim('"');
        Assert.Equal($"progres-postav-{game.Id}.xlsx", fileName);

        await using var stream = await response.Content.ReadAsStreamAsync();
        Assert.True(stream.Length > 0);
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheet("Progres");
        Assert.Equal("Hráč", worksheet.Cell(1, 1).GetString());
        Assert.Equal("Postava", worksheet.Cell(1, 2).GetString());
        Assert.Equal("Čas (Praha)", worksheet.Cell(1, 7).GetString());
        Assert.Equal("Eliška Export", worksheet.Cell(2, 1).GetString());
        Assert.StartsWith("Excel Hero", worksheet.Cell(2, 2).GetString(), StringComparison.Ordinal);
        Assert.Equal(new DateTime(2026, 5, 1, 10, 5, 0), worksheet.Cell(2, 7).GetDateTime());
        Assert.Equal("d.M.yyyy HH:mm", worksheet.Cell(2, 7).Style.DateFormat.Format);
    }

    private async Task<GameDetailDto> CreateGameAsync()
    {
        var response = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Progression stats", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GameDetailDto>())!;
    }

    private static Task<Kingdom> GetKingdomAsync(WorldDbContext db, string name) =>
        db.Kingdoms.SingleAsync(k => k.Name == name);

    private static List<GameTimeSlot> SeedTimeSlots(int gameId, DateTime start) =>
    [
        Slot(gameId, start, GameTimePhase.Early),
        Slot(gameId, start.AddHours(2), GameTimePhase.Early),
        Slot(gameId, start.AddHours(4), GameTimePhase.Midgame),
        Slot(gameId, start.AddDays(1), GameTimePhase.Midgame),
        Slot(gameId, start.AddDays(1).AddHours(2), GameTimePhase.Midgame),
        Slot(gameId, start.AddDays(1).AddHours(4), GameTimePhase.Lategame),
        Slot(gameId, start.AddDays(1).AddHours(6), GameTimePhase.Lategame),
        Slot(gameId, start.AddDays(1).AddHours(8), GameTimePhase.EndGame)
    ];

    private static GameTimeSlot Slot(int gameId, DateTime start, GameTimePhase phase) => new()
    {
        GameId = gameId,
        StartTime = start,
        Duration = TimeSpan.FromHours(2),
        Stage = phase,
        InGameYear = 1
    };

    private static string StatsLabel(GameTimeSlot slot) =>
        PragueTimeFormatter.FormatTimeSlotDisplay(
            slot.Stage,
            slot.InGameYear,
            slot.StartTime,
            (decimal)slot.Duration.TotalHours);

    private static Character Character(string name, string firstName, string lastName) => new()
    {
        Name = $"{name} {Guid.NewGuid():N}",
        PlayerFirstName = firstName,
        PlayerLastName = lastName,
        IsPlayedCharacter = true
    };

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

    private static CharacterEvent LevelUp(int assignmentId, DateTime timestampUtc, int level) => new()
    {
        CharacterAssignmentId = assignmentId,
        Timestamp = timestampUtc,
        OrganizerUserId = "org",
        OrganizerName = "Org",
        EventType = CharacterEventType.LevelUp,
        Data = JsonSerializer.Serialize(new { level })
    };

    private static CharacterEvent Mastery(int assignmentId, DateTime timestampUtc, string skillName) =>
        SkillEvent(assignmentId, timestampUtc, new { skill = skillName, mastery = true });

    private static CharacterEvent SkillEvent(int assignmentId, DateTime timestampUtc, object data) => new()
    {
        CharacterAssignmentId = assignmentId,
        Timestamp = timestampUtc,
        OrganizerUserId = "org",
        OrganizerName = "Org",
        EventType = CharacterEventType.SkillGained,
        Data = JsonSerializer.Serialize(data)
    };
}
