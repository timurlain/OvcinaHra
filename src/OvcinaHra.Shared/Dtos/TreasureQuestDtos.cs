using System.Text.Json.Serialization;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Extensions;

namespace OvcinaHra.Shared.Dtos;

// --- TreasureQuest DTOs ---

public record TreasureQuestListDto(int Id, string Title, GameTimePhase Difficulty, int? LocationId, int? SecretStashId, int GameId)
{
    [JsonIgnore]
    public string DifficultyDisplay => Difficulty.GetDisplayName();
}

public record TreasureQuestDetailDto(
    int Id, string Title, string? Clue, GameTimePhase Difficulty,
    int? LocationId, string? LocationName, int? SecretStashId, string? SecretStashName, int GameId,
    List<TreasureItemDto> Items);

public record CreateTreasureQuestDto(
    string Title, GameTimePhase Difficulty, int GameId,
    string? Clue = null, int? LocationId = null, int? SecretStashId = null);

public record UpdateTreasureQuestDto(
    string Title, string? Clue, GameTimePhase Difficulty,
    int? LocationId, int? SecretStashId);

public record TreasureItemDto(int Id, int ItemId, string ItemName, int Count, int? TreasureQuestId);
public record AddTreasureItemDto(int ItemId, int Count = 1);

public record PendingTreasureQuestDto(
    int QuestId,
    string QuestName,
    int ExpectedStashId,
    string ExpectedStashName,
    int? ExpectedLocationId,
    string? ExpectedLocationName,
    DateTime? IssuedAt,
    string? IssuedBy);

public record VerifyTreasureQuestDto(
    int StashId,
    double? MatchConfidence = null,
    bool Override = false,
    string? Reason = null);

// --- Treasure Pool DTOs ---

public record TreasurePoolItemDto(int Id, int ItemId, string ItemName, ItemType ItemType, int Count, int GameId, bool IsUnique)
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
    List<StashSummaryDto> Stashes,
    string? Region = null);

public record StashSummaryDto(int Id, string Name, int ItemCount);

public record AssignTreasureDto(
    string Title, GameTimePhase Difficulty, int GameId,
    string? Clue = null, int? LocationId = null, int? SecretStashId = null,
    List<int>? TreasureItemIds = null,
    List<UnlimitedItemAssignDto>? UnlimitedItems = null);

public record UnlimitedItemAssignDto(int ItemId, int Count = 1);

public record TreasureSummaryDto(
    int PoolRemaining, int Placed,
    int StartCount, int EarlyCount, int MidgameCount, int LategameCount);

public record UnlimitedItemDto(int ItemId, string ItemName, ItemType ItemType);

public record AvailablePoolItemDto(int ItemId, string DisplayName, int Remaining, int StockCount);

// ── Issue #160: bulk pool refill ──────────────────────────────────────────

/// <summary>
/// Result of <c>POST /api/treasure-planning/refill-pool/{gameId}</c>.
/// Sums every <c>GameItem.IsFindable</c> item's stock against allocations on
/// TreasureItem (pool + treasure-quest), QuestReward, and PersonalQuestItemReward,
/// then appends the unallocated remainder to the treasure pool. Items where
/// allocations already exceed stock are skipped and listed in
/// <see cref="OverAllocated"/> for manual review.
/// </summary>
public record RefillPoolResponse(
    int ItemsAdded,
    IReadOnlyList<RefillPoolItemDto> Added,
    IReadOnlyList<RefillPoolOverAllocationDto> OverAllocated);

public record RefillPoolItemDto(int ItemId, string ItemName, int Added);

public record RefillPoolOverAllocationDto(
    int ItemId,
    string ItemName,
    int StockCount,
    int Allocated,
    int Excess);
