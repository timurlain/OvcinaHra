using System.Text.Json.Serialization;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Extensions;

namespace OvcinaHra.Shared.Dtos;

public record PersonalQuestListDto(
    int Id,
    string Name,
    string? Description,
    TreasureQuestDifficulty Difficulty,
    bool AllowWarrior,
    bool AllowArcher,
    bool AllowMage,
    bool AllowThief,
    string? QuestCardText,
    string? RewardCardText,
    string? RewardNote,
    string? Notes,
    string? ImagePath,
    IReadOnlyList<int> SkillRewardIds,
    IReadOnlyList<PersonalQuestItemRewardSummary> ItemRewards)
{
    [JsonIgnore]
    public string DifficultyDisplay => Difficulty.GetDisplayName();

    [JsonIgnore]
    public string ClassRestrictionDisplay
    {
        get
        {
            if (!AllowWarrior && !AllowArcher && !AllowMage && !AllowThief)
                return "Všechna povolání";
            var parts = new List<string>();
            if (AllowWarrior) parts.Add(PlayerClass.Warrior.GetDisplayName());
            if (AllowArcher) parts.Add(PlayerClass.Archer.GetDisplayName());
            if (AllowMage) parts.Add(PlayerClass.Mage.GetDisplayName());
            if (AllowThief) parts.Add(PlayerClass.Thief.GetDisplayName());
            return string.Join(", ", parts);
        }
    }

    [JsonIgnore]
    public bool HasImage => !string.IsNullOrEmpty(ImagePath);
}

public record PersonalQuestItemRewardSummary(int ItemId, string ItemName, int Quantity);

public record PersonalQuestDetailDto(
    int Id,
    string Name,
    string? Description,
    TreasureQuestDifficulty Difficulty,
    bool AllowWarrior,
    bool AllowArcher,
    bool AllowMage,
    bool AllowThief,
    string? QuestCardText,
    string? RewardCardText,
    string? RewardNote,
    string? Notes,
    string? ImagePath,
    List<SkillRewardDto> SkillRewards,
    List<ItemRewardDto> ItemRewards);

public record SkillRewardDto(int SkillId, string SkillName);
public record ItemRewardDto(int ItemId, string ItemName, int Quantity);

public record CreatePersonalQuestDto(
    string Name,
    TreasureQuestDifficulty Difficulty,
    string? Description = null,
    bool AllowWarrior = false,
    bool AllowArcher = false,
    bool AllowMage = false,
    bool AllowThief = false,
    string? QuestCardText = null,
    string? RewardCardText = null,
    string? RewardNote = null,
    string? Notes = null);

public record UpdatePersonalQuestDto(
    string Name,
    TreasureQuestDifficulty Difficulty,
    string? Description,
    bool AllowWarrior,
    bool AllowArcher,
    bool AllowMage,
    bool AllowThief,
    string? QuestCardText,
    string? RewardCardText,
    string? RewardNote,
    string? Notes);

public record GamePersonalQuestListDto(
    int Id,            // PersonalQuest.Id, matches the ListDto for popup reuse
    string Name,
    string? Description,
    TreasureQuestDifficulty Difficulty,
    bool AllowWarrior,
    bool AllowArcher,
    bool AllowMage,
    bool AllowThief,
    string? QuestCardText,
    string? RewardCardText,
    string? RewardNote,
    string? Notes,
    string? ImagePath,
    int GameId,
    int XpCost,
    int? PerKingdomLimit,
    string? RewardSummary)
{
    [JsonIgnore]
    public string DifficultyDisplay => Difficulty.GetDisplayName();

    [JsonIgnore]
    public string ClassRestrictionDisplay
    {
        get
        {
            if (!AllowWarrior && !AllowArcher && !AllowMage && !AllowThief)
                return "Všechna povolání";
            var parts = new List<string>();
            if (AllowWarrior) parts.Add(PlayerClass.Warrior.GetDisplayName());
            if (AllowArcher) parts.Add(PlayerClass.Archer.GetDisplayName());
            if (AllowMage) parts.Add(PlayerClass.Mage.GetDisplayName());
            if (AllowThief) parts.Add(PlayerClass.Thief.GetDisplayName());
            return string.Join(", ", parts);
        }
    }

    [JsonIgnore]
    public bool HasImage => !string.IsNullOrEmpty(ImagePath);
}

public record CreateGamePersonalQuestDto(int GameId, int PersonalQuestId,
    int XpCost = 0, int? PerKingdomLimit = null);

public record UpdateGamePersonalQuestDto(int XpCost, int? PerKingdomLimit);

public record AddSkillRewardDto(int SkillId);
public record AddItemRewardDto(int ItemId, int Quantity = 1);
