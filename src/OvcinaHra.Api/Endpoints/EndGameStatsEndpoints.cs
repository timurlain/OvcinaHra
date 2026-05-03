using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OvcinaHra.Api.Data;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Domain.Rules;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Endpoints;

public static partial class GameEndpoints
{
    private const string NoKingdomName = "Bez království";
    private const int MaxStatsLevel = LevelingRules.MaxLevel + LevelingRules.MaxBuyableMasteries;

    private static async Task<Results<Ok<EndGameStatsDto>, NotFound>> GetEndGameStats(
        int gameId,
        WorldDbContext db)
    {
        if (!await db.Games.AsNoTracking().AnyAsync(g => g.Id == gameId))
            return TypedResults.NotFound();

        var kingdoms = await db.Kingdoms
            .AsNoTracking()
            .OrderBy(k => k.SortOrder)
            .ThenBy(k => k.Name)
            .Select(k => new EndGameKingdomRow(
                k.Id,
                k.Name,
                k.HexColor,
                k.SortOrder))
            .ToListAsync();

        var assignments = await db.CharacterAssignments
            .AsNoTracking()
            .Where(a => a.GameId == gameId && a.IsActive)
            .Select(a => new EndGameAssignmentRow(
                a.Id,
                a.Character.Name,
                a.Class,
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

        var parsedActivities = activities
            .Select(ParseActivity)
            .ToList();

        var monsterIds = parsedActivities
            .Select(a => a.MonsterId)
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
        var levelEvents = ExtractLevelEvents(parsedActivities);
        var currentLevelByAssignment = levelEvents
            .GroupBy(e => e.AssignmentId)
            .ToDictionary(g => g.Key, g => Math.Max(1, g.Max(e => e.Level)));
        var masteryCountByAssignment = await BuildMasteryCountByAssignment(
            db,
            assignments.Select(a => a.AssignmentId).ToArray(),
            assignmentById);

        var kingdomBreakdowns = BuildKingdomBreakdowns(
            kingdoms,
            assignments,
            currentLevelByAssignment,
            masteryCountByAssignment);
        var firstToLevel5 = BuildFirstToLevel5(levelEvents, assignmentById);
        var monsterStats = BuildMonsterStats(parsedActivities, assignmentById, monsterNamesById);

        var leaderboard = kingdomBreakdowns
            .Where(k => k.HeroCount > 0)
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
        List<EndGameKingdomRow> kingdoms,
        List<EndGameAssignmentRow> assignments,
        Dictionary<int, int> currentLevelByAssignment,
        Dictionary<int, int> masteryCountByAssignment)
    {
        var kingdomKeys = kingdoms
            .Select(k => new EndGameKingdomKey(
                k.KingdomId,
                k.KingdomName,
                StoredColorOrEmpty(k.KingdomColor),
                k.SortOrder))
            .ToList();

        if (assignments.Any(a => a.KingdomId is null))
            kingdomKeys.Add(new EndGameKingdomKey(null, NoKingdomName, string.Empty, int.MaxValue));

        var assignmentsByKingdomId = assignments.ToLookup(a => a.KingdomId);

        return kingdomKeys
            .OrderBy(k => k.SortOrder)
            .ThenBy(k => k.KingdomName)
            .Select(k =>
            {
                var kingdomAssignments = assignmentsByKingdomId[k.KingdomId];
                var levels = kingdomAssignments
                    .Select(a => EffectiveLevel(
                        a.AssignmentId,
                        currentLevelByAssignment,
                        masteryCountByAssignment))
                    .ToArray();
                var buckets = Enumerable.Range(1, MaxStatsLevel)
                    .Select(level => new LevelCountDto(
                        level,
                        LevelLabel(level),
                        levels.Count(heroLevel => heroLevel == level)))
                    .ToArray();
                var totalLevelsGained = levels.Sum(level => Math.Max(0, level - 1));
                var averageLevel = levels.Length == 0
                    ? 0
                    : Math.Round((decimal)levels.Average(), 2, MidpointRounding.AwayFromZero);

                return new KingdomLevelBreakdownDto(
                    k.KingdomId,
                    k.KingdomName,
                    k.ColorHex,
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
            StoredColorOrEmpty(assignment.KingdomColor),
            first.TimestampUtc);
    }

    private static MonsterStatsDto BuildMonsterStats(
        List<EndGameParsedActivityRow> activities,
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

    private static List<EndGameLevelEvent> ExtractLevelEvents(List<EndGameParsedActivityRow> activities)
    {
        var events = new List<EndGameLevelEvent>();
        foreach (var activity in activities)
        {
            if (activity.ActivityType != WorldActivityType.CharacterLevelUp
                || activity.CharacterAssignmentId is not { } assignmentId)
                continue;

            if (activity.Level is { } level)
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

    private static EndGameParsedActivityRow ParseActivity(EndGameActivityRow activity)
    {
        int? level = null;
        int? monsterId = null;
        string? monsterName = null;

        if (!string.IsNullOrWhiteSpace(activity.DataJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(activity.DataJson);
                level = TryReadInt(doc.RootElement, "level");
                monsterName = TryReadString(doc.RootElement, "monsterName")
                    ?? TryReadString(doc.RootElement, "monster");
                monsterId = TryReadInt(doc.RootElement, "monsterId")
                    ?? TryReadInt(doc.RootElement, "monsterNpcId");
            }
            catch (JsonException)
            {
                // Treat malformed audit JSON as missing optional stats fields.
            }
        }

        return new EndGameParsedActivityRow(
            activity.ActivityId,
            activity.TimestampUtc,
            activity.ActivityType,
            activity.CharacterAssignmentId,
            activity.Description,
            level,
            monsterName,
            monsterId);
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
                StoredColorOrEmpty(assignment.KingdomColor),
                assignment.SortOrder);
        }

        return new EndGameKingdomKey(null, NoKingdomName, string.Empty, int.MaxValue);
    }

    private static string MonsterName(
        EndGameParsedActivityRow activity,
        Dictionary<int, string> monsterNamesById)
    {
        if (!string.IsNullOrWhiteSpace(activity.MonsterName))
            return activity.MonsterName;

        if (activity.MonsterId is { } id && monsterNamesById.TryGetValue(id, out var name))
            return name;

        if (MonsterNameFromDescription(activity.Description) is { } descriptionName)
            return descriptionName;

        if (activity.MonsterId is { } unknownId)
            return $"Nestvůra #{unknownId}";

        return "Neznámá nestvůra";
    }

    private static string? MonsterNameFromDescription(string description)
    {
        var colonIndex = description.LastIndexOf(':');
        if (colonIndex < 0 || colonIndex == description.Length - 1) return null;

        var name = description[(colonIndex + 1)..].Trim();
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    private static async Task<Dictionary<int, int>> BuildMasteryCountByAssignment(
        WorldDbContext db,
        int[] assignmentIds,
        Dictionary<int, EndGameAssignmentRow> assignmentById)
    {
        if (assignmentIds.Length == 0)
            return [];

        var skillEvents = await db.CharacterEvents
            .AsNoTracking()
            .Where(e => assignmentIds.Contains(e.CharacterAssignmentId)
                && e.EventType == CharacterEventType.SkillGained)
            .Select(e => new EndGameSkillEventRow(e.CharacterAssignmentId, e.Data))
            .ToListAsync();

        return skillEvents
            .Where(e => IsMasterySkillEvent(e, assignmentById))
            .GroupBy(e => e.AssignmentId)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    private static bool IsMasterySkillEvent(
        EndGameSkillEventRow skillEvent,
        Dictionary<int, EndGameAssignmentRow> assignmentById)
    {
        if (string.IsNullOrWhiteSpace(skillEvent.Data))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(skillEvent.Data);
            if (TryReadBool(doc.RootElement, "mastery") is { } mastery)
                return mastery;

            var skillName = TryReadString(doc.RootElement, "skill");
            if (string.IsNullOrWhiteSpace(skillName)
                || !assignmentById.TryGetValue(skillEvent.AssignmentId, out var assignment)
                || assignment.Class is not { } playerClass)
                return false;

            return LevelingRules.BuyableMasteriesAtL5(playerClass)
                .Any(m => string.Equals(m.Name, skillName, StringComparison.OrdinalIgnoreCase));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static int EffectiveLevel(
        int assignmentId,
        Dictionary<int, int> currentLevelByAssignment,
        Dictionary<int, int> masteryCountByAssignment)
    {
        var level = Math.Clamp(
            CurrentLevel(assignmentId, currentLevelByAssignment),
            1,
            LevelingRules.MaxLevel);

        if (level < LevelingRules.MaxLevel)
            return level;

        var masteryCount = masteryCountByAssignment.TryGetValue(assignmentId, out var count)
            ? Math.Min(count, LevelingRules.MaxBuyableMasteries)
            : 0;

        return level + masteryCount;
    }

    private static string StoredColorOrEmpty(string? storedHex) =>
        string.IsNullOrWhiteSpace(storedHex) ? string.Empty : storedHex;

    private static string LevelLabel(int level) =>
        level > LevelingRules.MaxLevel
            ? $"({level.ToString(CultureInfo.InvariantCulture)})"
            : level.ToString(CultureInfo.InvariantCulture);

    private static int? TryReadInt(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property))
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

    private static string? TryReadString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(property.GetString())
                ? property.GetString()
                : null;
    }

    private static bool? TryReadBool(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property))
            return null;

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(property.GetString(), out var value) => value,
            _ => null
        };
    }

    private sealed record EndGameKingdomRow(
        int KingdomId,
        string KingdomName,
        string? KingdomColor,
        int SortOrder);

    private sealed record EndGameAssignmentRow(
        int AssignmentId,
        string HeroName,
        PlayerClass? Class,
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

    private sealed record EndGameParsedActivityRow(
        int ActivityId,
        DateTime TimestampUtc,
        WorldActivityType ActivityType,
        int? CharacterAssignmentId,
        string Description,
        int? Level,
        string? MonsterName,
        int? MonsterId);

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

    private sealed record EndGameSkillEventRow(int AssignmentId, string Data);
}
