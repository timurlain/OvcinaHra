using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Shared.Dtos;

public record OrganizerRoleMatrixDto(
    int GameId,
    List<OrganizerRoleTimeSlotDto> TimeSlots,
    List<OrganizerRoleNpcDto> Npcs,
    List<OrganizerRoleAssignmentDto> Assignments);

public record OrganizerRoleTimeSlotDto(
    int Id,
    DateTime StartTime,
    decimal DurationHours,
    int? InGameYear,
    GameTimePhase Stage);

public record OrganizerRoleNpcDto(
    int NpcId,
    string NpcName,
    NpcRole Role,
    string? Description);

public record OrganizerRoleAssignmentDto(
    int Id,
    int GameId,
    int GameTimeSlotId,
    int NpcId,
    int PersonId,
    string PersonName,
    string? PersonEmail,
    string? Notes,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public record UpsertOrganizerRoleAssignmentDto(
    int PersonId,
    string PersonName,
    string? PersonEmail,
    string? Notes = null);

public record BulkOrganizerRoleAssignmentDto(
    int NpcId,
    int PersonId,
    string PersonName,
    string? PersonEmail,
    string? Notes = null);

public record BulkOrganizerRoleAssignmentResultDto(
    int CreatedCount,
    int UpdatedCount,
    List<OrganizerRoleAssignmentDto> Assignments);
