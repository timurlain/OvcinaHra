using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Shared.Dtos;

public record QuestListDto(int Id, string Name, QuestType QuestType, int? ChainOrder, int? ParentQuestId, int? GameId);

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
    List<TagDto> Tags,
    List<QuestLocationDto> Locations,
    List<QuestEncounterDto> Encounters,
    List<QuestRewardDto> Rewards);

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

public record QuestLocationDto(int QuestId, int LocationId, string LocationName);
public record QuestEncounterDto(int QuestId, int MonsterId, string MonsterName, int Quantity);
public record QuestRewardDto(int QuestId, int ItemId, string ItemName, int Quantity);

public record AddQuestLocationDto(int LocationId);
public record AddQuestEncounterDto(int MonsterId, int Quantity = 1);
public record AddQuestRewardDto(int ItemId, int Quantity = 1);

public record QuestCatalogDto(
    int Id, string Name, QuestType QuestType,
    int? GameId, string? GameName, int? GameEdition,
    string? Description, string? FullText, string? RewardSummary);
public record QuestCopyResultDto(QuestListDto Quest, List<string> Warnings);
public record MoveQuestToGameDto(int GameId);
