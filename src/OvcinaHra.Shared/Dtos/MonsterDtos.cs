using System.Text.Json.Serialization;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Extensions;

namespace OvcinaHra.Shared.Dtos;

public record MonsterListDto(
    int Id, string Name, MonsterType MonsterType, int Category,
    int Attack, int Defense, int Health,
    int? RewardXp, int? RewardMoney,
    string? Abilities, string? AiBehavior, string? RewardNotes, string? Notes,
    List<string> TagNames,
    string? ImagePath = null, string? ImageUrl = null)
{
    [JsonIgnore]
    public string MonsterTypeDisplay => MonsterType.GetDisplayName();

    [JsonIgnore]
    public string TagsDisplay => string.Join(", ", TagNames);
}

public record MonsterDetailDto(
    int Id,
    string Name,
    int Category,
    MonsterType MonsterType,
    string? Abilities,
    string? AiBehavior,
    int Attack,
    int Defense,
    int Health,
    int? RewardXp,
    int? RewardMoney,
    string? RewardNotes,
    string? Notes,
    string? ImagePath,
    List<TagDto> Tags);

public record CreateMonsterDto(
    string Name,
    int Category,
    MonsterType MonsterType,
    int Attack,
    int Defense,
    int Health,
    string? Abilities = null,
    string? AiBehavior = null,
    int? RewardXp = null,
    int? RewardMoney = null,
    string? RewardNotes = null,
    string? Notes = null);

public record UpdateMonsterDto(
    string Name,
    int Category,
    MonsterType MonsterType,
    int Attack,
    int Defense,
    int Health,
    string? Abilities,
    string? AiBehavior,
    int? RewardXp,
    int? RewardMoney,
    string? RewardNotes,
    string? Notes = null);

// Per-game monster assignment
public record GameMonsterDto(int GameId, int MonsterId, string MonsterName);

public record CreateGameMonsterDto(int GameId, int MonsterId);

// Loot
public record MonsterLootDto(int MonsterId, int ItemId, string ItemName, int GameId, int Quantity);

public record CreateMonsterLootDto(int MonsterId, int ItemId, int GameId, int Quantity = 1);
