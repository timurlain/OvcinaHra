using System.Text.Json.Serialization;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Extensions;

namespace OvcinaHra.Shared.Dtos;

// --- TreasureQuest DTOs ---

public record TreasureQuestListDto(int Id, string Title, TreasureQuestDifficulty Difficulty, int? LocationId, int? SecretStashId, int GameId)
{
    [JsonIgnore]
    public string DifficultyDisplay => Difficulty.GetDisplayName();
}

public record TreasureQuestDetailDto(
    int Id, string Title, string? Clue, TreasureQuestDifficulty Difficulty,
    int? LocationId, string? LocationName, int? SecretStashId, string? SecretStashName, int GameId,
    List<TreasureItemDto> Items);

public record CreateTreasureQuestDto(
    string Title, TreasureQuestDifficulty Difficulty, int GameId,
    string? Clue = null, int? LocationId = null, int? SecretStashId = null);

public record UpdateTreasureQuestDto(
    string Title, string? Clue, TreasureQuestDifficulty Difficulty,
    int? LocationId, int? SecretStashId);

public record TreasureItemDto(int Id, int ItemId, string ItemName, int Count, int? TreasureQuestId);
public record AddTreasureItemDto(int ItemId, int Count = 1);

// --- Treasure Pool DTOs ---

public record TreasurePoolItemDto(int Id, int ItemId, string ItemName, ItemType ItemType, int Count, int GameId)
{
    [JsonIgnore]
    public string ItemTypeDisplay => ItemType.GetDisplayName();
}
public record CreateTreasurePoolItemDto(int ItemId, int GameId, int Count = 1);

// --- Treasure Planning DTOs ---

public record TreasurePlanningLocationDto(
    int LocationId, string LocationName, LocationKind LocationKind,
    int StartCount, int EarlyCount, int MidgameCount, int LategameCount,
    int TotalItems, int StashCount, int MaxStashes,
    List<StashSummaryDto> Stashes);

public record StashSummaryDto(int Id, string Name, int ItemCount);

public record AssignTreasureDto(
    string Title, TreasureQuestDifficulty Difficulty, int GameId,
    string? Clue = null, int? LocationId = null, int? SecretStashId = null,
    List<int>? TreasureItemIds = null,
    List<UnlimitedItemAssignDto>? UnlimitedItems = null);

public record UnlimitedItemAssignDto(int ItemId, int Count = 1);

public record TreasureSummaryDto(
    int PoolRemaining, int Placed,
    int StartCount, int EarlyCount, int MidgameCount, int LategameCount);

public record UnlimitedItemDto(int ItemId, string ItemName, ItemType ItemType);

public record AvailablePoolItemDto(int ItemId, string DisplayName, int Remaining, int StockCount);
