using System.Text.Json.Serialization;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Extensions;

namespace OvcinaHra.Shared.Dtos;

public record GameTimeSlotDto(
    int Id,
    int? InGameYear,
    DateTime StartTime,
    decimal DurationHours,
    string? Rules,
    int? BattlefieldBonusId,
    int GameId,
    GameTimePhase Stage = GameTimePhase.Start,
    string? BattlefieldBonusName = null)
{
    public IReadOnlyList<LinkedGameEventDto> LinkedEvents { get; init; } = [];

    [JsonIgnore] public string StageDisplay => Stage.GetDisplayName();
    [JsonIgnore] public DateTime EndTime => StartTime.AddHours((double)DurationHours);
}

public record LinkedGameEventDto(int Id, string Name, GameEventKind Kind);

public record CreateGameTimeSlotDto(
    int GameId,
    DateTime StartTime,
    decimal DurationHours,
    int? InGameYear = null,
    string? Rules = null,
    int? BattlefieldBonusId = null,
    GameTimePhase Stage = GameTimePhase.Start,
    IReadOnlyList<int>? LinkedGameEventIds = null);

// Stage is nullable for backward compatibility — older PUT payloads omit it
// and we don't want to silently overwrite an existing Stage with `Start`.
// Same semantic as LinkedGameEventIds: null means "don't change".
public record UpdateGameTimeSlotDto(
    DateTime StartTime,
    decimal DurationHours,
    int? InGameYear,
    string? Rules,
    int? BattlefieldBonusId,
    GameTimePhase? Stage = null,
    IReadOnlyList<int>? LinkedGameEventIds = null);

public record BattlefieldBonusDto(int Id, string? Name, int AttackBonus, int DefenseBonus, string? Description, string? ImagePath, int GameId);

public record CreateBattlefieldBonusDto(int GameId, int AttackBonus = 0, int DefenseBonus = 0, string? Name = null, string? Description = null);

public record UpdateBattlefieldBonusDto(string? Name, int AttackBonus, int DefenseBonus, string? Description);
