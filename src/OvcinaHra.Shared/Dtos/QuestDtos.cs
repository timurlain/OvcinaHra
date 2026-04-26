using System.Text.Json.Serialization;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Extensions;

namespace OvcinaHra.Shared.Dtos;

public record QuestListDto(
    int Id,
    string Name,
    QuestType QuestType,
    int? ChainOrder,
    int? ParentQuestId,
    int? GameId,
    QuestState State,
    string? ImagePath,
    string? ImageUrl,
    int EncountersCount,
    int RewardsCount,
    int LocationsCount,
    int TagsCount)
{
    [JsonIgnore] public string QuestTypeDisplay => QuestType.GetDisplayName();
    [JsonIgnore] public string StateDisplay => State.GetDisplayName();
}

public record QuestDetailDto(
    int Id,
    string Name,
    QuestType QuestType,
    string? Description,
    string? FullText,
    string? TimeSlot,
    int? RewardXp,
    int? RewardMoney,
    string? RewardNotes,
    int? ChainOrder,
    int? ParentQuestId,
    int? GameId,
    QuestState State,
    string? ImagePath,
    string? ImageUrl,
    List<TagDto> Tags,
    List<QuestLocationDto> Locations,
    List<QuestEncounterDto> Encounters,
    List<QuestRewardDto> Rewards)
{
    [JsonIgnore] public string QuestTypeDisplay => QuestType.GetDisplayName();
    [JsonIgnore] public string StateDisplay => State.GetDisplayName();
}

public record CreateQuestDto(
    string Name,
    QuestType QuestType,
    int? GameId = null,
    string? Description = null,
    string? FullText = null,
    string? TimeSlot = null,
    int? RewardXp = null,
    int? RewardMoney = null,
    string? RewardNotes = null,
    int? ChainOrder = null,
    int? ParentQuestId = null);

public record UpdateQuestDto(
    string Name,
    QuestType QuestType,
    string? Description,
    string? FullText,
    string? TimeSlot,
    int? RewardXp,
    int? RewardMoney,
    string? RewardNotes,
    int? ChainOrder,
    int? ParentQuestId);

public record UpdateQuestStateDto(QuestState State);

public record QuestLocationDto(int QuestId, int LocationId, string LocationName);
public record QuestEncounterDto(int QuestId, int MonsterId, string MonsterName, int Quantity);
public record QuestRewardDto(int QuestId, int ItemId, string ItemName, int Quantity);

public record AddQuestLocationDto(int LocationId);
public record AddQuestEncounterDto(int MonsterId, int Quantity = 1);
public record AddQuestRewardDto(int ItemId, int Quantity = 1);

// Issue #214 — ordered location waypoints inside a quest. Drives the
// Map page's animated quest path (deferred from #207 pending this).
public record QuestWaypointDto(
    int Id, int QuestId, int LocationId, string LocationName,
    int Order, string? Label);
public record AddQuestWaypointDto(int LocationId, string? Label = null);
public record UpdateQuestWaypointDto(int LocationId, string? Label);
/// <summary>Bulk reorder by sending the waypoint ids in target order;
/// server sets <c>Order = arrayIndex + 1</c> in a deferred-style two-pass
/// update so the (QuestId, Order) unique index isn't violated transiently.</summary>
public record ReorderQuestWaypointsDto(IReadOnlyList<int> WaypointIdsInOrder);

public record QuestCatalogDto(
    int Id, string Name, QuestType QuestType,
    int? GameId, string? GameName, int? GameEdition,
    string? Description, string? FullText, string? RewardSummary,
    string? ImagePath, string? ImageUrl,
    int EncountersCount, int RewardsCount)
{
    [JsonIgnore]
    public string QuestTypeDisplay => QuestType.GetDisplayName();
}
public record QuestCopyResultDto(QuestListDto Quest, List<string> Warnings);
public record MoveQuestToGameDto(int GameId);
