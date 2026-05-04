using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OvcinaHra.Api.Data;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Domain.Rules;
using OvcinaHra.Shared.Dtos;
using OvcinaHra.Shared.Extensions;

namespace OvcinaHra.Api.Endpoints;

public static partial class GameEndpoints
{
    private const string ProgressionXlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private static async Task<Results<Ok<ProgressionStatsDto>, NotFound>> GetProgressionStats(
        int gameId,
        WorldDbContext db,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("OvcinaHra.Api.Endpoints.ProgressionStats");
        logger.LogInformation("[progression] entry gameId={GameId}", gameId);

        var stats = await BuildProgressionStatsAsync(gameId, db, logger, ct);
        return stats is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(stats);
    }

    private static async Task<Results<FileContentHttpResult, NotFound>> DownloadProgressionStatsXlsx(
        int gameId,
        WorldDbContext db,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("OvcinaHra.Api.Endpoints.ProgressionStats");
        logger.LogInformation("[progression] entry xlsx gameId={GameId}", gameId);

        var stats = await BuildProgressionStatsAsync(gameId, db, logger, ct);
        if (stats is null)
            return TypedResults.NotFound();

        try
        {
            logger.LogInformation("[progression] xlsx write start gameId={GameId} rows={RowCount}", gameId, stats.Events.Length);
            var timer = Stopwatch.StartNew();
            var bytes = WriteProgressionWorkbook(stats);
            timer.Stop();
            logger.LogInformation(
                "[progression] xlsx write complete gameId={GameId} rows={RowCount} elapsedMs={ElapsedMs} bytes={Bytes}",
                gameId,
                stats.Events.Length,
                timer.ElapsedMilliseconds,
                bytes.Length);

            return TypedResults.File(
                bytes,
                ProgressionXlsxContentType,
                fileDownloadName: $"progres-postav-{gameId.ToString(CultureInfo.InvariantCulture)}.xlsx");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[progression] xlsx write failed gameId={GameId}", gameId);
            throw;
        }
    }

    private static async Task<ProgressionStatsDto?> BuildProgressionStatsAsync(
        int gameId,
        WorldDbContext db,
        ILogger logger,
        CancellationToken ct)
    {
        if (!await db.Games.AsNoTracking().AnyAsync(g => g.Id == gameId, ct))
            return null;

        var kingdoms = await db.Kingdoms
            .AsNoTracking()
            .OrderBy(k => k.SortOrder)
            .ThenBy(k => k.Name)
            .Select(k => new ProgressionKingdomRow(k.Id, k.Name, k.HexColor, k.SortOrder))
            .ToListAsync(ct);

        var timeSlots = await db.GameTimeSlots
            .AsNoTracking()
            .Where(s => s.GameId == gameId)
            .OrderBy(s => s.StartTime)
            .ThenBy(s => s.Id)
            .Select(s => new ProgressionTimeSlotRow(
                s.Id,
                s.StartTime,
                s.Duration,
                s.Stage,
                s.InGameYear,
                TimeSlotDisplayExtensions.FormatTimeSlotDisplay(
                    s.Stage,
                    s.InGameYear,
                    s.StartTime,
                    (decimal)s.Duration.TotalHours)))
            .ToListAsync(ct);

        logger.LogInformation("[progression] CharacterEvent query start gameId={GameId}", gameId);
        var timer = Stopwatch.StartNew();
        var events = await db.CharacterEvents
            .AsNoTracking()
            .Where(e => e.Assignment.GameId == gameId
                && (e.EventType == CharacterEventType.LevelUp
                    || e.EventType == CharacterEventType.SkillGained))
            .OrderBy(e => e.Timestamp)
            .ThenBy(e => e.Id)
            .Select(e => new ProgressionCharacterEventRow(
                e.Id,
                e.CharacterAssignmentId,
                e.Timestamp,
                e.EventType,
                e.Data,
                e.Assignment.Character.Name,
                e.Assignment.Character.PlayerFirstName,
                e.Assignment.Character.PlayerLastName,
                e.Assignment.KingdomId,
                e.Assignment.Kingdom != null ? e.Assignment.Kingdom.Name : NoKingdomName,
                e.Assignment.Kingdom != null ? e.Assignment.Kingdom.HexColor : null))
            .ToListAsync(ct);
        timer.Stop();
        logger.LogInformation(
            "[progression] CharacterEvent query complete gameId={GameId} elapsedMs={ElapsedMs} rowCount={RowCount}",
            gameId,
            timer.ElapsedMilliseconds,
            events.Count);

        var normalizedEvents = BuildProgressionEvents(events, timeSlots, logger);
        var chartKingdoms = BuildProgressionKingdoms(kingdoms, timeSlots, normalizedEvents);
        var eventRows = normalizedEvents
            .Select(e => new ProgressionEventRow(
                e.PlayerName,
                e.CharacterName,
                e.KingdomName,
                e.KingdomHexColor,
                e.TimeSlotId,
                e.TimeSlotLabel,
                e.EventType,
                e.LevelGained,
                e.TimestampUtc))
            .OrderBy(e => e.TimestampUtc)
            .ThenBy(e => e.CharacterName)
            .ToArray();

        return new ProgressionStatsDto(chartKingdoms, eventRows);
    }

    private static List<ProgressionNormalizedEvent> BuildProgressionEvents(
        List<ProgressionCharacterEventRow> events,
        List<ProgressionTimeSlotRow> timeSlots,
        ILogger logger)
    {
        var normalized = new List<ProgressionNormalizedEvent>();
        var masteryCountByAssignment = new Dictionary<int, int>();
        var fallbackLevelByAssignment = new Dictionary<int, int>();
        var extraMasteryWarningLogged = false;

        foreach (var ev in events)
        {
            var levelGained = ev.EventType switch
            {
                CharacterEventType.LevelUp => ReadLevelUpLevel(ev, fallbackLevelByAssignment, logger),
                CharacterEventType.SkillGained when IsMasterySkillEvent(ev.Data, ev.EventId, logger) =>
                    ReadMasteryLevel(ev, masteryCountByAssignment, logger, ref extraMasteryWarningLogged),
                _ => null
            };

            if (levelGained is not { } level)
                continue;

            if (ev.KingdomId is null)
                continue;

            var bucket = ResolveProgressionBucket(ev.TimestampUtc, timeSlots, logger);
            if (bucket is null)
                continue;

            normalized.Add(new ProgressionNormalizedEvent(
                ev.EventId,
                ev.AssignmentId,
                ev.KingdomId.Value,
                ev.KingdomName,
                StoredColorOrEmpty(ev.KingdomHexColor),
                bucket.Id,
                timeSlots.IndexOf(bucket),
                bucket.Label,
                PlayerFullName(ev.PlayerFirstName, ev.PlayerLastName) ?? "Neznámý hráč",
                ev.CharacterName,
                ev.EventType == CharacterEventType.LevelUp ? "LevelUp" : "MasterySkillGained",
                level,
                ev.TimestampUtc));
        }

        return normalized;
    }

    private static int? ReadLevelUpLevel(
        ProgressionCharacterEventRow ev,
        Dictionary<int, int> fallbackLevelByAssignment,
        ILogger logger)
    {
        int? dataLevel = null;
        if (!string.IsNullOrWhiteSpace(ev.Data))
        {
            try
            {
                using var doc = JsonDocument.Parse(ev.Data);
                dataLevel = TryReadInt(doc.RootElement, "level");
            }
            catch (JsonException)
            {
                logger.LogWarning(
                    "[progression] malformed LevelUp data eventId={EventId} assignmentId={AssignmentId}",
                    ev.EventId,
                    ev.AssignmentId);
                dataLevel = null;
            }
        }

        var nextFallback = fallbackLevelByAssignment.TryGetValue(ev.AssignmentId, out var current)
            ? current + 1
            : 1;
        var level = dataLevel ?? nextFallback;
        level = Math.Clamp(level, 1, LevelingRules.MaxLevel);
        fallbackLevelByAssignment[ev.AssignmentId] = Math.Max(nextFallback, level);
        return level;
    }

    private static int? ReadMasteryLevel(
        ProgressionCharacterEventRow ev,
        Dictionary<int, int> masteryCountByAssignment,
        ILogger logger,
        ref bool extraMasteryWarningLogged)
    {
        var masteryCount = masteryCountByAssignment.TryGetValue(ev.AssignmentId, out var current)
            ? current + 1
            : 1;
        masteryCountByAssignment[ev.AssignmentId] = masteryCount;

        if (masteryCount > LevelingRules.MaxBuyableMasteries)
        {
            if (!extraMasteryWarningLogged)
            {
                logger.LogWarning(
                    "[progression] skipped extra mastery event assignmentId={AssignmentId} eventId={EventId}",
                    ev.AssignmentId,
                    ev.EventId);
                extraMasteryWarningLogged = true;
            }

            return null;
        }

        return LevelingRules.MaxLevel + masteryCount;
    }

    private static bool IsMasterySkillEvent(string data, int eventId, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(data))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(data);
            return TryReadBool(doc.RootElement, "mastery") == true;
        }
        catch (JsonException)
        {
            logger.LogWarning("[progression] malformed SkillGained data eventId={EventId}", eventId);
            return false;
        }
    }

    private static ProgressionTimeSlotRow? ResolveProgressionBucket(
        DateTime timestampUtc,
        List<ProgressionTimeSlotRow> timeSlots,
        ILogger logger)
    {
        if (timeSlots.Count == 0)
            return null;

        if (timeSlots.Count > 1 && timestampUtc >= timeSlots[^1].StartTime)
        {
            var penultimate = timeSlots[^2];
            logger.LogDebug(
                "[progression] bucket fallback reason=final-or-post-game timestampUtc={TimestampUtc:o} bucketId={TimeSlotId}",
                timestampUtc,
                penultimate.Id);
            return penultimate;
        }

        var natural = timeSlots.FirstOrDefault(s =>
            timestampUtc >= s.StartTime && timestampUtc < s.StartTime + s.Duration);
        if (natural is not null)
            return natural;

        var previous = timeSlots.LastOrDefault(s => s.StartTime < timestampUtc);
        if (previous is not null)
        {
            logger.LogDebug(
                "[progression] bucket fallback reason=previous-slot timestampUtc={TimestampUtc:o} bucketId={TimeSlotId}",
                timestampUtc,
                previous.Id);
            return previous;
        }

        return null;
    }

    private static KingdomProgressionDto[] BuildProgressionKingdoms(
        List<ProgressionKingdomRow> kingdoms,
        List<ProgressionTimeSlotRow> timeSlots,
        List<ProgressionNormalizedEvent> events)
    {
        var stateByAssignment = new Dictionary<int, ProgressionAssignmentState>();
        var eventsByBucket = events
            .GroupBy(e => e.TimeSlotIndex)
            .ToDictionary(g => g.Key, g => g.OrderBy(e => e.TimestampUtc).ThenBy(e => e.EventId).ToList());
        var countsByKingdomAndBucket = kingdoms.ToDictionary(
            k => k.KingdomId,
            _ => Enumerable.Range(0, timeSlots.Count)
                .Select(_ => new int[MaxStatsLevel])
                .ToArray());

        for (var bucketIndex = 0; bucketIndex < timeSlots.Count; bucketIndex++)
        {
            if (eventsByBucket.TryGetValue(bucketIndex, out var bucketEvents))
            {
                foreach (var ev in bucketEvents)
                    stateByAssignment[ev.AssignmentId] = new ProgressionAssignmentState(ev.KingdomId, ev.LevelGained);
            }

            foreach (var state in stateByAssignment.Values)
            {
                if (!countsByKingdomAndBucket.TryGetValue(state.KingdomId, out var bucketCounts))
                    continue;

                bucketCounts[bucketIndex][state.Level - 1]++;
            }
        }

        return kingdoms
            .OrderBy(k => k.SortOrder)
            .ThenBy(k => k.KingdomName)
            .Select(k => new KingdomProgressionDto(
                k.KingdomId,
                k.KingdomName,
                StoredColorOrEmpty(k.KingdomHexColor),
                k.SortOrder,
                timeSlots
                    .Select((slot, index) => new TimeSlotBucketDto(
                        slot.Id,
                        slot.Label,
                        countsByKingdomAndBucket[k.KingdomId][index]))
                    .ToArray()))
            .ToArray();
    }

    private static byte[] WriteProgressionWorkbook(ProgressionStatsDto stats)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Progres");
        string[] headers =
        [
            "Hráč",
            "Postava",
            "Království",
            "Časový blok",
            "Událost",
            "Získaná úroveň",
            "Čas UTC"
        ];

        for (var column = 0; column < headers.Length; column++)
            worksheet.Cell(1, column + 1).Value = headers[column];

        var rowIndex = 2;
        foreach (var row in stats.Events)
        {
            worksheet.Cell(rowIndex, 1).Value = row.PlayerName;
            worksheet.Cell(rowIndex, 2).Value = row.CharacterName;
            worksheet.Cell(rowIndex, 3).Value = row.KingdomName;
            worksheet.Cell(rowIndex, 4).Value = row.TimeSlotLabel;
            worksheet.Cell(rowIndex, 5).Value = row.EventType;
            worksheet.Cell(rowIndex, 6).Value = row.LevelGained;
            worksheet.Cell(rowIndex, 7).Value = row.TimestampUtc;
            rowIndex++;
        }

        var usedRange = worksheet.Range(1, 1, Math.Max(1, rowIndex - 1), headers.Length);
        worksheet.Range(1, 1, 1, headers.Length).Style.Font.Bold = true;
        usedRange.SetAutoFilter();
        worksheet.SheetView.FreezeRows(1);
        worksheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static string? PlayerFullName(string? firstName, string? lastName)
    {
        var fullName = $"{firstName} {lastName}".Trim();
        return string.IsNullOrWhiteSpace(fullName) ? null : fullName;
    }

    private sealed record ProgressionKingdomRow(
        int KingdomId,
        string KingdomName,
        string? KingdomHexColor,
        int SortOrder);

    private sealed record ProgressionTimeSlotRow(
        int Id,
        DateTime StartTime,
        TimeSpan Duration,
        GameTimePhase Stage,
        int? InGameYear,
        string Label);

    private sealed record ProgressionCharacterEventRow(
        int EventId,
        int AssignmentId,
        DateTime TimestampUtc,
        CharacterEventType EventType,
        string Data,
        string CharacterName,
        string? PlayerFirstName,
        string? PlayerLastName,
        int? KingdomId,
        string KingdomName,
        string? KingdomHexColor);

    private sealed record ProgressionNormalizedEvent(
        int EventId,
        int AssignmentId,
        int KingdomId,
        string KingdomName,
        string KingdomHexColor,
        int TimeSlotId,
        int TimeSlotIndex,
        string TimeSlotLabel,
        string PlayerName,
        string CharacterName,
        string EventType,
        int LevelGained,
        DateTime TimestampUtc);

    private sealed record ProgressionAssignmentState(int KingdomId, int Level);
}
