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

public record OrganizerRoleMeDto(
    int GameId,
    string UserId,
    string? UserEmail,
    int? PersonId,
    string? PersonName,
    int? CurrentSlotId,
    OrganizerRoleMePrimaryRoleDto? PrimaryRole,
    List<OrganizerRoleMeSlotDto> TimeSlots);

public record OrganizerRoleMePrimaryRoleDto(
    int NpcId,
    string NpcName,
    NpcRole Role,
    string? Description,
    int AssignmentCount);

public record OrganizerRoleMeSlotDto(
    int SlotId,
    DateTime StartTime,
    decimal DurationHours,
    int? InGameYear,
    GameTimePhase Stage,
    List<OrganizerRoleMeSlotAssignmentDto> Assignments);

public record OrganizerRoleMeSlotAssignmentDto(
    int NpcId,
    string NpcName,
    NpcRole Role,
    string? Description,
    string? Notes);

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

public record BulkFillRemainingOrganizerRoleDto(
    int GameId,
    int PersonId,
    int? RoleNpcId = null);

public record BulkFillRemainingOrganizerRoleResultDto(
    int InsertedCount,
    int SkippedCount,
    int DefaultNpcId,
    string DefaultNpcName);
