using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using OvcinaHra.Api.Data;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Endpoints;

public static class OrganizerRoleEndpoints
{
    private const string LoggerCategory = "OvcinaHra.Api.Endpoints.OrganizerRoleEndpoints";
    private const int PersonNameMaxLength = 200;
    private const int PersonEmailMaxLength = 200;
    private const int NotesMaxLength = 1000;

    public static RouteGroupBuilder MapOrganizerRoleEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/games/{gameId:int}/organizer-role-assignments")
            .WithTags("OrganizerRoles");

        group.MapGet("/", GetMatrix);
        group.MapPost("/bulk", BulkAssign);
        group.MapPut("/slots/{slotId:int}/npcs/{npcId:int}", UpsertSlotAssignment);
        group.MapDelete("/slots/{slotId:int}/npcs/{npcId:int}", DeleteSlotAssignment);

        return group;
    }

    private static async Task<Results<Ok<OrganizerRoleMatrixDto>, NotFound>> GetMatrix(
        int gameId,
        WorldDbContext db,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger(LoggerCategory);
        if (!await db.Games.AnyAsync(g => g.Id == gameId, ct))
        {
            logger.LogInformation("[organizer-roles] matrix game_not_found gameId={GameId}", gameId);
            return TypedResults.NotFound();
        }

        var slots = await db.GameTimeSlots
            .AsNoTracking()
            .Where(s => s.GameId == gameId)
            .OrderBy(s => s.StartTime)
            .Select(s => new OrganizerRoleTimeSlotDto(
                s.Id,
                s.StartTime,
                (decimal)s.Duration.TotalHours,
                s.InGameYear,
                s.Stage))
            .ToListAsync(ct);

        var npcs = await db.GameNpcs
            .AsNoTracking()
            .Where(gn => gn.GameId == gameId)
            .OrderBy(gn => gn.Npc.Role)
            .ThenBy(gn => gn.Npc.Name)
            .Select(gn => new OrganizerRoleNpcDto(
                gn.NpcId,
                gn.Npc.Name,
                gn.Npc.Role,
                gn.Npc.Description))
            .ToListAsync(ct);

        var assignments = await db.OrganizerRoleAssignments
            .AsNoTracking()
            .Where(a => a.GameId == gameId)
            .OrderBy(a => a.GameTimeSlotId)
            .ThenBy(a => a.NpcId)
            .Select(a => new OrganizerRoleAssignmentDto(
                a.Id,
                a.GameId,
                a.GameTimeSlotId,
                a.NpcId,
                a.PersonId,
                a.PersonName,
                a.PersonEmail,
                a.Notes,
                a.CreatedAtUtc,
                a.UpdatedAtUtc))
            .ToListAsync(ct);

        logger.LogInformation(
            "[organizer-roles] matrix loaded gameId={GameId} slots={SlotCount} npcs={NpcCount} assignments={AssignmentCount}",
            gameId,
            slots.Count,
            npcs.Count,
            assignments.Count);

        return TypedResults.Ok(new OrganizerRoleMatrixDto(gameId, slots, npcs, assignments));
    }

    private static async Task<Results<Ok<BulkOrganizerRoleAssignmentResultDto>, NotFound, ProblemHttpResult>> BulkAssign(
        int gameId,
        BulkOrganizerRoleAssignmentDto dto,
        WorldDbContext db,
        ILoggerFactory loggerFactory,
        HttpContext http,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger(LoggerCategory);
        var serverTimer = Stopwatch.StartNew();
        logger.LogInformation(
            "[organizer-roles-server] entry method={Method} path={Path} route=bulk-assign gameId={GameId} npcId={NpcId} personId={PersonId}",
            http.Request.Method,
            http.Request.Path,
            gameId,
            dto.NpcId,
            dto.PersonId);

        void LogServerExit(string status, int? createdCount = null, int? updatedCount = null, string? detail = null)
        {
            serverTimer.Stop();
            logger.LogInformation(
                "[organizer-roles-server] exit method={Method} path={Path} route=bulk-assign gameId={GameId} npcId={NpcId} personId={PersonId} status={Status} created={CreatedCount} updated={UpdatedCount} elapsedMs={ElapsedMs} detail={Detail}",
                http.Request.Method,
                http.Request.Path,
                gameId,
                dto.NpcId,
                dto.PersonId,
                status,
                createdCount,
                updatedCount,
                serverTimer.ElapsedMilliseconds,
                detail);
        }

        try
        {
            var validation = await ValidateAssignmentTargetAsync(
                gameId,
                dto.NpcId,
                dto.PersonId,
                dto.PersonName,
                dto.PersonEmail,
                dto.Notes,
                db,
                ct);
            if (validation is not null)
            {
                LogServerExit("validation");
                return validation;
            }

            var slotIds = await db.GameTimeSlots
                .Where(s => s.GameId == gameId)
                .OrderBy(s => s.StartTime)
                .Select(s => s.Id)
                .ToListAsync(ct);

            if (slotIds.Count == 0)
            {
                LogServerExit("validation", detail: "no-slots");
                return Problem(
                    "Nejdřív vytvořte časové bloky v harmonogramu.",
                    "Hra nemá časové sloty",
                    StatusCodes.Status400BadRequest);
            }

            var existing = await db.OrganizerRoleAssignments
                .Where(a => a.GameId == gameId && a.NpcId == dto.NpcId && slotIds.Contains(a.GameTimeSlotId))
                .ToDictionaryAsync(a => a.GameTimeSlotId, ct);

            var now = DateTime.UtcNow;
            var created = 0;
            var updated = 0;
            foreach (var slotId in slotIds)
            {
                if (existing.TryGetValue(slotId, out var assignment))
                {
                    assignment.PersonId = dto.PersonId;
                    assignment.PersonName = dto.PersonName.Trim();
                    assignment.PersonEmail = NullIfWhiteSpace(dto.PersonEmail);
                    assignment.Notes = NullIfWhiteSpace(dto.Notes);
                    assignment.UpdatedAtUtc = now;
                    updated++;
                    continue;
                }

                db.OrganizerRoleAssignments.Add(new OrganizerRoleAssignment
                {
                    GameId = gameId,
                    GameTimeSlotId = slotId,
                    NpcId = dto.NpcId,
                    PersonId = dto.PersonId,
                    PersonName = dto.PersonName.Trim(),
                    PersonEmail = NullIfWhiteSpace(dto.PersonEmail),
                    Notes = NullIfWhiteSpace(dto.Notes),
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });
                created++;
            }

            logger.LogInformation(
                "[organizer-roles-server] db-write.attempt route=bulk-assign gameId={GameId} npcId={NpcId} created={CreatedCount} updated={UpdatedCount}",
                gameId,
                dto.NpcId,
                created,
                updated);
            var dbTimer = Stopwatch.StartNew();
            await db.SaveChangesAsync(ct);
            dbTimer.Stop();
            logger.LogInformation(
                "[organizer-roles-server] db-write.ok route=bulk-assign gameId={GameId} npcId={NpcId} elapsedMs={ElapsedMs}",
                gameId,
                dto.NpcId,
                dbTimer.ElapsedMilliseconds);

            var assignments = await LoadAssignmentsForNpcAsync(gameId, dto.NpcId, db, ct);
            logger.LogInformation(
                "[organizer-roles] bulk assigned gameId={GameId} npcId={NpcId} personId={PersonId} created={CreatedCount} updated={UpdatedCount}",
                gameId,
                dto.NpcId,
                dto.PersonId,
                created,
                updated);

            LogServerExit("ok", created, updated);
            return TypedResults.Ok(new BulkOrganizerRoleAssignmentResultDto(created, updated, assignments));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "[organizer-roles-server] exception method={Method} path={Path} route=bulk-assign gameId={GameId} npcId={NpcId} personId={PersonId} elapsedMs={ElapsedMs} detail={Detail}",
                http.Request.Method,
                http.Request.Path,
                gameId,
                dto.NpcId,
                dto.PersonId,
                serverTimer.ElapsedMilliseconds,
                ex.Message);
            LogServerExit("exception", detail: ex.Message);
            throw;
        }
    }

    private static async Task<Results<Ok<OrganizerRoleAssignmentDto>, NotFound, ProblemHttpResult>> UpsertSlotAssignment(
        int gameId,
        int slotId,
        int npcId,
        UpsertOrganizerRoleAssignmentDto dto,
        WorldDbContext db,
        ILoggerFactory loggerFactory,
        HttpContext http,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger(LoggerCategory);
        var serverTimer = Stopwatch.StartNew();
        logger.LogInformation(
            "[organizer-roles-server] entry method={Method} path={Path} route=slot-assign gameId={GameId} slotId={SlotId} npcId={NpcId} personId={PersonId}",
            http.Request.Method,
            http.Request.Path,
            gameId,
            slotId,
            npcId,
            dto.PersonId);

        void LogServerExit(string status, string? operation = null, string? detail = null)
        {
            serverTimer.Stop();
            logger.LogInformation(
                "[organizer-roles-server] exit method={Method} path={Path} route=slot-assign gameId={GameId} slotId={SlotId} npcId={NpcId} personId={PersonId} operation={Operation} status={Status} elapsedMs={ElapsedMs} detail={Detail}",
                http.Request.Method,
                http.Request.Path,
                gameId,
                slotId,
                npcId,
                dto.PersonId,
                operation,
                status,
                serverTimer.ElapsedMilliseconds,
                detail);
        }

        try
        {
            var validation = await ValidateAssignmentTargetAsync(
                gameId,
                npcId,
                dto.PersonId,
                dto.PersonName,
                dto.PersonEmail,
                dto.Notes,
                db,
                ct,
                slotId);
            if (validation is not null)
            {
                LogServerExit("validation");
                return validation;
            }

            var existing = await db.OrganizerRoleAssignments
                .SingleOrDefaultAsync(a => a.GameId == gameId && a.GameTimeSlotId == slotId && a.NpcId == npcId, ct);

            var operation = existing is null ? "create" : "update";
            var now = DateTime.UtcNow;
            if (existing is null)
            {
                existing = new OrganizerRoleAssignment
                {
                    GameId = gameId,
                    GameTimeSlotId = slotId,
                    NpcId = npcId,
                    PersonId = dto.PersonId,
                    PersonName = dto.PersonName.Trim(),
                    PersonEmail = NullIfWhiteSpace(dto.PersonEmail),
                    Notes = NullIfWhiteSpace(dto.Notes),
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                };
                db.OrganizerRoleAssignments.Add(existing);
            }
            else
            {
                existing.PersonId = dto.PersonId;
                existing.PersonName = dto.PersonName.Trim();
                existing.PersonEmail = NullIfWhiteSpace(dto.PersonEmail);
                existing.Notes = NullIfWhiteSpace(dto.Notes);
                existing.UpdatedAtUtc = now;
            }

            logger.LogInformation(
                "[organizer-roles-server] db-write.attempt route=slot-assign gameId={GameId} slotId={SlotId} npcId={NpcId} operation={Operation}",
                gameId,
                slotId,
                npcId,
                operation);
            var dbTimer = Stopwatch.StartNew();
            await db.SaveChangesAsync(ct);
            dbTimer.Stop();
            logger.LogInformation(
                "[organizer-roles-server] db-write.ok route=slot-assign gameId={GameId} slotId={SlotId} npcId={NpcId} operation={Operation} elapsedMs={ElapsedMs}",
                gameId,
                slotId,
                npcId,
                operation,
                dbTimer.ElapsedMilliseconds);
            logger.LogInformation(
                "[organizer-roles] slot assigned gameId={GameId} slotId={SlotId} npcId={NpcId} personId={PersonId}",
                gameId,
                slotId,
                npcId,
                dto.PersonId);

            LogServerExit("ok", operation);
            return TypedResults.Ok(ToDto(existing));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "[organizer-roles-server] exception method={Method} path={Path} route=slot-assign gameId={GameId} slotId={SlotId} npcId={NpcId} personId={PersonId} elapsedMs={ElapsedMs} detail={Detail}",
                http.Request.Method,
                http.Request.Path,
                gameId,
                slotId,
                npcId,
                dto.PersonId,
                serverTimer.ElapsedMilliseconds,
                ex.Message);
            LogServerExit("exception", detail: ex.Message);
            throw;
        }
    }

    private static async Task<Results<NoContent, NotFound>> DeleteSlotAssignment(
        int gameId,
        int slotId,
        int npcId,
        WorldDbContext db,
        ILoggerFactory loggerFactory,
        HttpContext http,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger(LoggerCategory);
        var serverTimer = Stopwatch.StartNew();
        logger.LogInformation(
            "[organizer-roles-server] entry method={Method} path={Path} route=slot-remove gameId={GameId} slotId={SlotId} npcId={NpcId}",
            http.Request.Method,
            http.Request.Path,
            gameId,
            slotId,
            npcId);

        void LogServerExit(string status, int? personId = null, string? detail = null)
        {
            serverTimer.Stop();
            logger.LogInformation(
                "[organizer-roles-server] exit method={Method} path={Path} route=slot-remove gameId={GameId} slotId={SlotId} npcId={NpcId} personId={PersonId} status={Status} elapsedMs={ElapsedMs} detail={Detail}",
                http.Request.Method,
                http.Request.Path,
                gameId,
                slotId,
                npcId,
                personId,
                status,
                serverTimer.ElapsedMilliseconds,
                detail);
        }

        try
        {
            var assignment = await db.OrganizerRoleAssignments
                .SingleOrDefaultAsync(a => a.GameId == gameId && a.GameTimeSlotId == slotId && a.NpcId == npcId, ct);
            if (assignment is null)
            {
                LogServerExit("not-found");
                return TypedResults.NotFound();
            }

            db.OrganizerRoleAssignments.Remove(assignment);
            logger.LogInformation(
                "[organizer-roles-server] db-write.attempt route=slot-remove gameId={GameId} slotId={SlotId} npcId={NpcId} personId={PersonId}",
                gameId,
                slotId,
                npcId,
                assignment.PersonId);
            var dbTimer = Stopwatch.StartNew();
            await db.SaveChangesAsync(ct);
            dbTimer.Stop();
            logger.LogInformation(
                "[organizer-roles-server] db-write.ok route=slot-remove gameId={GameId} slotId={SlotId} npcId={NpcId} personId={PersonId} elapsedMs={ElapsedMs}",
                gameId,
                slotId,
                npcId,
                assignment.PersonId,
                dbTimer.ElapsedMilliseconds);

            logger.LogInformation(
                "[organizer-roles] slot unassigned gameId={GameId} slotId={SlotId} npcId={NpcId} personId={PersonId}",
                gameId,
                slotId,
                npcId,
                assignment.PersonId);

            LogServerExit("no-content", assignment.PersonId);
            return TypedResults.NoContent();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "[organizer-roles-server] exception method={Method} path={Path} route=slot-remove gameId={GameId} slotId={SlotId} npcId={NpcId} elapsedMs={ElapsedMs} detail={Detail}",
                http.Request.Method,
                http.Request.Path,
                gameId,
                slotId,
                npcId,
                serverTimer.ElapsedMilliseconds,
                ex.Message);
            LogServerExit("exception", detail: ex.Message);
            throw;
        }
    }

    private static async Task<ProblemHttpResult?> ValidateAssignmentTargetAsync(
        int gameId,
        int npcId,
        int personId,
        string personName,
        string? personEmail,
        string? notes,
        WorldDbContext db,
        CancellationToken ct,
        int? slotId = null)
    {
        if (!await db.Games.AnyAsync(g => g.Id == gameId, ct))
        {
            return Problem(
                "Požadovaná hra nebyla nalezena.",
                "Hra neexistuje",
                StatusCodes.Status404NotFound);
        }

        if (personId <= 0 || string.IsNullOrWhiteSpace(personName))
        {
            return Problem(
                "Vyberte dospělého z registrace.",
                "Chybí dospělý",
                StatusCodes.Status400BadRequest);
        }

        if (personName.Trim().Length > PersonNameMaxLength)
        {
            return Problem(
                $"Jméno dospělého může mít nejvýše {PersonNameMaxLength} znaků.",
                "Jméno dospělého je příliš dlouhé",
                StatusCodes.Status400BadRequest);
        }

        if (!string.IsNullOrWhiteSpace(personEmail) && personEmail.Trim().Length > PersonEmailMaxLength)
        {
            return Problem(
                $"E-mail dospělého může mít nejvýše {PersonEmailMaxLength} znaků.",
                "E-mail dospělého je příliš dlouhý",
                StatusCodes.Status400BadRequest);
        }

        if (!string.IsNullOrWhiteSpace(notes) && notes.Trim().Length > NotesMaxLength)
        {
            return Problem(
                $"Poznámka může mít nejvýše {NotesMaxLength} znaků.",
                "Poznámka je příliš dlouhá",
                StatusCodes.Status400BadRequest);
        }

        if (slotId is int sid && !await db.GameTimeSlots.AnyAsync(s => s.Id == sid && s.GameId == gameId, ct))
        {
            return Problem(
                "Časový slot nepatří k této hře.",
                "Neplatný časový slot",
                StatusCodes.Status400BadRequest);
        }

        if (!await db.GameNpcs.AnyAsync(gn => gn.GameId == gameId && gn.NpcId == npcId, ct))
        {
            return Problem(
                "NPC role není přiřazená k této hře.",
                "Neplatná NPC role",
                StatusCodes.Status400BadRequest);
        }

        return null;
    }

    private static async Task<List<OrganizerRoleAssignmentDto>> LoadAssignmentsForNpcAsync(
        int gameId,
        int npcId,
        WorldDbContext db,
        CancellationToken ct)
    {
        return await db.OrganizerRoleAssignments
            .AsNoTracking()
            .Where(a => a.GameId == gameId && a.NpcId == npcId)
            .OrderBy(a => a.GameTimeSlotId)
            .Select(a => new OrganizerRoleAssignmentDto(
                a.Id,
                a.GameId,
                a.GameTimeSlotId,
                a.NpcId,
                a.PersonId,
                a.PersonName,
                a.PersonEmail,
                a.Notes,
                a.CreatedAtUtc,
                a.UpdatedAtUtc))
            .ToListAsync(ct);
    }

    private static OrganizerRoleAssignmentDto ToDto(OrganizerRoleAssignment a) =>
        new(
            a.Id,
            a.GameId,
            a.GameTimeSlotId,
            a.NpcId,
            a.PersonId,
            a.PersonName,
            a.PersonEmail,
            a.Notes,
            a.CreatedAtUtc,
            a.UpdatedAtUtc);

    private static ProblemHttpResult Problem(string detail, string title, int statusCode) =>
        TypedResults.Problem(detail: detail, title: title, statusCode: statusCode);

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
