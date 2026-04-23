using System.Text.Json.Serialization;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Extensions;

namespace OvcinaHra.Shared.Dtos;

public record LocationListDto(
    int Id,
    string Name,
    LocationKind LocationKind,
    string? Region,
    decimal? Latitude,
    decimal? Longitude,
    int? ParentLocationId,
    string? Description,
    string? Details,
    string? GamePotential,
    string? NpcInfo,
    string? SetupNotes,
    IReadOnlyList<LocationStashDto> Stashes,
    IReadOnlyList<LocationQuestDto> Quests,
    IReadOnlyList<LocationTreasureQuestDto> LocationTreasureQuests,
    string? ImagePath = null,
    string? ImageUrl = null)
{
    [JsonIgnore]
    public string LocationKindDisplay => LocationKind.GetDisplayName();
}

public record LocationStashDto(
    int Id,
    string Name,
    IReadOnlyList<LocationTreasureQuestDto> TreasureQuests,
    string? ImagePath = null,
    string? ImageUrl = null);

public record LocationQuestDto(
    int Id,
    string Name,
    QuestType QuestType,
    string? Description,
    string? FullText,
    int? RewardXp,
    int? RewardMoney,
    string? RewardNotes,
    IReadOnlyList<LocationQuestRewardItemDto> ItemRewards)
{
    [JsonIgnore]
    public string QuestTypeDisplay => QuestType.GetDisplayName();
}

public record LocationQuestRewardItemDto(
    int ItemId,
    string ItemName,
    int Quantity);

public record LocationTreasureQuestDto(
    int Id,
    string Title,
    string? Clue,
    TreasureQuestDifficulty Difficulty)
{
    [JsonIgnore]
    public string DifficultyDisplay => Difficulty.GetDisplayName();
}

public record LocationDetailDto(
    int Id,
    string Name,
    string? Description,
    string? Details,
    string? GamePotential,
    string? Prompt,
    string? Region,
    LocationKind LocationKind,
    decimal? Latitude,
    decimal? Longitude,
    string? ImagePath,
    string? PlacementPhotoPath,
    string? NpcInfo,
    string? SetupNotes,
    int? ParentLocationId,
    List<LocationVariantDto> Variants);

public record LocationVariantDto(int Id, string Name, LocationKind LocationKind);

public record CreateLocationDto(
    string Name,
    LocationKind LocationKind,
    decimal? Latitude = null,
    decimal? Longitude = null,
    string? Description = null,
    string? Details = null,
    string? GamePotential = null,
    string? Prompt = null,
    string? Region = null,
    string? NpcInfo = null,
    string? SetupNotes = null,
    int? ParentLocationId = null);

public record UpdateLocationDto(
    string Name,
    LocationKind LocationKind,
    decimal? Latitude = null,
    decimal? Longitude = null,
    string? Description = null,
    string? Details = null,
    string? GamePotential = null,
    string? Prompt = null,
    string? Region = null,
    string? NpcInfo = null,
    string? SetupNotes = null,
    int? ParentLocationId = null);

public record GameLocationDto(int GameId, int LocationId);
