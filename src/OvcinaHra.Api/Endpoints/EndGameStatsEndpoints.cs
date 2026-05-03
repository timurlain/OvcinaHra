using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OvcinaHra.Api.Data;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Endpoints;

public static partial class GameEndpoints
{
    private const string NoKingdomName = "Bez království";
    private const string NoKingdomColor = "#6A6A6A";

    private static async Task<Results<Ok<EndGameStatsDto>, NotFound>> GetEndGameStats(
        int gameId,
        WorldDbContext db)
    {
        if (!await db.Games.AsNoTracking().AnyAsync(g => g.Id == gameId))
            return TypedResults.NotFound();

        var assignments = await db.CharacterAssignments
            .AsNoTracking()
            .Where(a => a.GameId == gameId && a.IsActive)
            .Select(a => new EndGameAssignmentRow(
                a.Id,
                a.Character.Name,
                a.KingdomId,
                a.Kingdom != null ? a.Kingdom.Name : NoKingdomName,
                a.Kingdom != null ? a.Kingdom.HexColor : null,
                a.Kingdom != null ? a.Kingdom.SortOrder : int.MaxValue))
            .ToListAsync();

        var activities = await db.WorldActivities
            .AsNoTracking()
            .Where(a => a.GameId == gameId
                && (a.ActivityType == WorldActivityType.CharacterLevelUp
                    || a.ActivityType == WorldActivityType.MonsterDefeated
                    || a.ActivityType == WorldActivityType.HeroFell))
            .Select(a => new EndGameActivityRow(
                a.Id,
                a.TimestampUtc,
                a.ActivityType,
                a.CharacterAssignmentId,
                a.Description,
                a.DataJson))
            .ToListAsync();

        var monsterIds = activities
            .Select(a => TryReadInt(a.DataJson, "monsterId"))
            .OfType<int>()
            .Distinct()
            .ToArray();
        var monsterNamesById = monsterIds.Length == 0
            ? new Dictionary<int, string>()
            : await db.Monsters
                .AsNoTracking()
                .Where(m => monsterIds.Contains(m.Id))
                .ToDictionaryAsync(m => m.Id, m => m.Name);

        var assignmentById = assignments.ToDictionary(a => a.AssignmentId);
        var levelEvents = ExtractLevelEvents(activities);
        var currentLevelByAssignment = levelEvents
            .GroupBy(e => e.AssignmentId)
            .ToDictionary(g => g.Key, g => Math.Max(1, g.Max(e => e.Level)));

        var kingdomBreakdowns = BuildKingdomBreakdowns(assignments, currentLevelByAssignment);
        var firstToLevel5 = BuildFirstToLevel5(levelEvents, assignmentById);
        var monsterStats = BuildMonsterStats(activities, assignmentById, monsterNamesById);

        var leaderboard = kingdomBreakdowns
            .Select(k => new KingdomLeaderboardEntryDto(
                k.KingdomId,
                k.KingdomName,
                k.ColorHex,
                k.HeroCount,
                k.TotalLevelsGained,
                k.AverageLevel))
            .OrderByDescending(k => k.TotalLevelsGained)
            .ThenByDescending(k => k.AverageLevel)
            .ThenBy(k => k.KingdomName)
            .ToArray();

        return TypedResults.Ok(new EndGameStatsDto(
            kingdomBreakdowns,
            firstToLevel5,
            leaderboard,
            monsterStats));
    }

    private static KingdomLevelBreakdownDto[] BuildKingdomBreakdowns(
        List<EndGameAssignmentRow> assignments,
        Dictionary<int, int> currentLevelByAssignment)
    {
        return assignments
            .GroupBy(a => new EndGameKingdomKey(
                a.KingdomId,
                a.KingdomName,
                CanonKingdomColor(a.KingdomName, a.KingdomColor),
                a.SortOrder))
            .OrderBy(g => g.Key.SortOrder)
            .ThenBy(g => g.Key.KingdomName)
            .Select(g =>
            {
                var levels = g
                    .Select(a => CurrentLevel(a.AssignmentId, currentLevelByAssignment))
                    .ToArray();
                var buckets = Enumerable.Range(1, 5)
                    .Select(level => new LevelCountDto(
                        level,
                        LevelLabel(level),
                        levels.Count(heroLevel => Math.Min(heroLevel, 5) == level)))
                    .ToArray();
                var totalLevelsGained = levels.Sum(level => Math.Max(0, level - 1));
                var averageLevel = levels.Length == 0
                    ? 0
                    : Math.Round((decimal)levels.Average(), 2, MidpointRounding.AwayFromZero);

                return new KingdomLevelBreakdownDto(
                    g.Key.KingdomId,
                    g.Key.KingdomName,
                    g.Key.ColorHex,
                    levels.Length,
                    totalLevelsGained,
                    averageLevel,
                    buckets);
            })
            .ToArray();
    }

    private static FirstToLevel5Dto? BuildFirstToLevel5(
        List<EndGameLevelEvent> levelEvents,
        Dictionary<int, EndGameAssignmentRow> assignmentById)
    {
        var first = levelEvents
            .Where(e => e.Level == 5 && assignmentById.ContainsKey(e.AssignmentId))
            .OrderBy(e => e.TimestampUtc)
            .ThenBy(e => e.ActivityId)
            .FirstOrDefault();
        if (first is null) return null;

        var assignment = assignmentById[first.AssignmentId];
        return new FirstToLevel5Dto(
            assignment.AssignmentId,
            assignment.HeroName,
            assignment.KingdomId,
            assignment.KingdomName,
            CanonKingdomColor(assignment.KingdomName, assignment.KingdomColor),
            first.TimestampUtc);
    }

    private static MonsterStatsDto BuildMonsterStats(
        List<EndGameActivityRow> activities,
        Dictionary<int, EndGameAssignmentRow> assignmentById,
        Dictionary<int, string> monsterNamesById)
    {
        var defeated = activities
            .Where(a => a.ActivityType == WorldActivityType.MonsterDefeated)
            .Select(a => MonsterName(a, monsterNamesById))
            .GroupBy(name => name)
            .Select(g => new MonsterDefeatEntryDto(g.Key, g.Count()))
            .OrderByDescending(e => e.Count)
            .ThenBy(e => e.MonsterName)
            .ToArray();

        var fallen = activities
            .Where(a => a.ActivityType == WorldActivityType.HeroFell)
            .GroupBy(a => KingdomFor(a.CharacterAssignmentId, assignmentById))
            .Select(g => new HeroFellEntryDto(
                g.Key.KingdomId,
                g.Key.KingdomName,
                g.Key.ColorHex,
                g.Count()))
            .OrderByDescending(e => e.Count)
            .ThenBy(e => e.KingdomName)
            .ToArray();

        return new MonsterStatsDto(
            defeated,
            fallen,
            defeated.Sum(e => e.Count),
            fallen.Sum(e => e.Count));
    }

    private static List<EndGameLevelEvent> ExtractLevelEvents(List<EndGameActivityRow> activities)
    {
        var events = new List<EndGameLevelEvent>();
        foreach (var activity in activities)
        {
            if (activity.ActivityType != WorldActivityType.CharacterLevelUp
                || activity.CharacterAssignmentId is not { } assignmentId)
                continue;

            if (TryReadInt(activity.DataJson, "level") is { } level)
            {
                events.Add(new EndGameLevelEvent(
                    activity.ActivityId,
                    assignmentId,
                    level,
                    activity.TimestampUtc));
            }
        }
        return events;
    }

    private static int CurrentLevel(int assignmentId, Dictionary<int, int> currentLevelByAssignment) =>
        currentLevelByAssignment.TryGetValue(assignmentId, out var level) ? Math.Max(1, level) : 1;

    private static EndGameKingdomKey KingdomFor(
        int? assignmentId,
        Dictionary<int, EndGameAssignmentRow> assignmentById)
    {
        if (assignmentId is { } id && assignmentById.TryGetValue(id, out var assignment))
        {
            return new EndGameKingdomKey(
                assignment.KingdomId,
                assignment.KingdomName,
                CanonKingdomColor(assignment.KingdomName, assignment.KingdomColor),
                assignment.SortOrder);
        }

        return new EndGameKingdomKey(null, NoKingdomName, NoKingdomColor, int.MaxValue);
    }

    private static string MonsterName(
        EndGameActivityRow activity,
        Dictionary<int, string> monsterNamesById)
    {
        if (TryReadString(activity.DataJson, "monsterName") is { } jsonName)
            return jsonName;
        if (TryReadString(activity.DataJson, "monster") is { } monster)
            return monster;

        var monsterId = TryReadInt(activity.DataJson, "monsterId")
            ?? TryReadInt(activity.DataJson, "monsterNpcId");
        if (monsterId is { } id)
            return monsterNamesById.TryGetValue(id, out var name) ? name : $"Nestvůra #{id}";

        return "Neznámá nestvůra";
    }

    private static string CanonKingdomColor(string kingdomName, string? storedHex) =>
        kingdomName switch
        {
            "Esgaroth" => "#242F3D",
            "Aradhryand" => "#243525",
            "Azanulinbar" => "#8C2423",
            "Arnor" => "#504B25",
            NoKingdomName => NoKingdomColor,
            _ when !string.IsNullOrWhiteSpace(storedHex) => storedHex,
            _ => NoKingdomColor
        };

    private static string LevelLabel(int level) =>
        level >= 5 ? "5+" : level.ToString(CultureInfo.InvariantCulture);

    private static int? TryReadInt(string? dataJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(dataJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(dataJson);
            if (!doc.RootElement.TryGetProperty(propertyName, out var property))
                return null;

            return property.ValueKind switch
            {
                JsonValueKind.Number when property.TryGetInt32(out var value) => value,
                JsonValueKind.String when int.TryParse(
                    property.GetString(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var value) => value,
                _ => null
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? TryReadString(string? dataJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(dataJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(dataJson);
            return doc.RootElement.TryGetProperty(propertyName, out var property)
                && property.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(property.GetString())
                    ? property.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record EndGameAssignmentRow(
        int AssignmentId,
        string HeroName,
        int? KingdomId,
        string KingdomName,
        string? KingdomColor,
        int SortOrder);

    private sealed record EndGameActivityRow(
        int ActivityId,
        DateTime TimestampUtc,
        WorldActivityType ActivityType,
        int? CharacterAssignmentId,
        string Description,
        string? DataJson);

    private sealed record EndGameLevelEvent(
        int ActivityId,
        int AssignmentId,
        int Level,
        DateTime TimestampUtc);

    private sealed record EndGameKingdomKey(
        int? KingdomId,
        string KingdomName,
        string ColorHex,
        int SortOrder);
}
