using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OvcinaHra.Api.Data;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Endpoints;

public static class OrganizerRoleEndpoints
{
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
        var logger = loggerFactory.CreateLogger("OrganizerRoleEndpoints");
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
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("OrganizerRoleEndpoints");
        var validation = await ValidateAssignmentTargetAsync(gameId, dto.NpcId, dto.PersonId, dto.PersonName, db, ct);
        if (validation is not null) return validation;

        var slotIds = await db.GameTimeSlots
            .Where(s => s.GameId == gameId)
            .OrderBy(s => s.StartTime)
            .Select(s => s.Id)
            .ToListAsync(ct);

        if (slotIds.Count == 0)
        {
            return Problem(
                "Hra nemá žádné časové sloty.",
                "Nejdřív vytvořte časové bloky v harmonogramu.",
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

        await db.SaveChangesAsync(ct);
        var assignments = await LoadAssignmentsForNpcAsync(gameId, dto.NpcId, db, ct);
        logger.LogInformation(
            "[organizer-roles] bulk assigned gameId={GameId} npcId={NpcId} personId={PersonId} created={CreatedCount} updated={UpdatedCount}",
            gameId,
            dto.NpcId,
            dto.PersonId,
            created,
            updated);

        return TypedResults.Ok(new BulkOrganizerRoleAssignmentResultDto(created, updated, assignments));
    }

    private static async Task<Results<Ok<OrganizerRoleAssignmentDto>, NotFound, ProblemHttpResult>> UpsertSlotAssignment(
        int gameId,
        int slotId,
        int npcId,
        UpsertOrganizerRoleAssignmentDto dto,
        WorldDbContext db,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("OrganizerRoleEndpoints");
        var validation = await ValidateAssignmentTargetAsync(gameId, npcId, dto.PersonId, dto.PersonName, db, ct, slotId);
        if (validation is not null) return validation;

        var existing = await db.OrganizerRoleAssignments
            .SingleOrDefaultAsync(a => a.GameId == gameId && a.GameTimeSlotId == slotId && a.NpcId == npcId, ct);

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

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "[organizer-roles] slot assigned gameId={GameId} slotId={SlotId} npcId={NpcId} personId={PersonId}",
            gameId,
            slotId,
            npcId,
            dto.PersonId);

        return TypedResults.Ok(ToDto(existing));
    }

    private static async Task<Results<NoContent, NotFound>> DeleteSlotAssignment(
        int gameId,
        int slotId,
        int npcId,
        WorldDbContext db,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var assignment = await db.OrganizerRoleAssignments
            .SingleOrDefaultAsync(a => a.GameId == gameId && a.GameTimeSlotId == slotId && a.NpcId == npcId, ct);
        if (assignment is null) return TypedResults.NotFound();

        db.OrganizerRoleAssignments.Remove(assignment);
        await db.SaveChangesAsync(ct);

        loggerFactory.CreateLogger("OrganizerRoleEndpoints").LogInformation(
            "[organizer-roles] slot unassigned gameId={GameId} slotId={SlotId} npcId={NpcId} personId={PersonId}",
            gameId,
            slotId,
            npcId,
            assignment.PersonId);

        return TypedResults.NoContent();
    }

    private static async Task<ProblemHttpResult?> ValidateAssignmentTargetAsync(
        int gameId,
        int npcId,
        int personId,
        string personName,
        WorldDbContext db,
        CancellationToken ct,
        int? slotId = null)
    {
        if (personId <= 0 || string.IsNullOrWhiteSpace(personName))
        {
            return Problem(
                "Vyberte dospělého z registrace.",
                "Chybí dospělý",
                StatusCodes.Status400BadRequest);
        }

        if (!await db.Games.AnyAsync(g => g.Id == gameId, ct))
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound);

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
