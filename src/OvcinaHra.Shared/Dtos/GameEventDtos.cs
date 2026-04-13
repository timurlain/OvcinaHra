using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Shared.Dtos;

public record GameEventListDto(
    int Id, int GameId, string Name, string? Description,
    int TimeSlotCount, int LocationCount, int QuestCount, int NpcCount);

public record GameEventDetailDto(
    int Id, int GameId, string Name, string? Description,
    List<GameEventTimeSlotDto> TimeSlots,
    List<GameEventLocationRefDto> Locations,
    List<GameEventQuestRefDto> Quests,
    List<GameEventNpcRefDto> Npcs,
    DateTime CreatedAtUtc, DateTime UpdatedAtUtc);

public record GameEventTimeSlotDto(int TimeSlotId, DateTime StartTime, decimal DurationHours, int? InGameYear);
public record GameEventLocationRefDto(int LocationId, string LocationName);
public record GameEventQuestRefDto(int QuestId, string QuestName);
public record GameEventNpcRefDto(
    int NpcId, string NpcName, NpcRole Role, string? RoleInEvent,
    string? PlayedByName, string? PlayedByEmail);

public record CreateGameEventDto(
    string Name, string? Description,
    List<int> TimeSlotIds,
    List<int> LocationIds,
    List<int> QuestIds,
    List<CreateGameEventNpcDto> Npcs);

public record CreateGameEventNpcDto(int NpcId, string? RoleInEvent = null);

public record UpdateGameEventDto(
    string Name, string? Description,
    List<int> TimeSlotIds,
    List<int> LocationIds,
    List<int> QuestIds,
    List<CreateGameEventNpcDto> Npcs);

// Bot schedule response
public record UserScheduleDto(
    int PersonId, int GameId,
    List<UserScheduleEventDto> Events);

public record UserScheduleEventDto(
    int EventId, string EventName, string? Description,
    List<GameEventTimeSlotDto> TimeSlots,
    List<string> LocationNames,
    List<string> QuestNames,
    string NpcName, NpcRole NpcRole, string? NpcRoleInEvent);
