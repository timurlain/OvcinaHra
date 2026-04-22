using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Shared.Domain.Entities;

public class PersonalQuest
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public TreasureQuestDifficulty Difficulty { get; set; }

    public bool AllowWarrior { get; set; }
    public bool AllowArcher { get; set; }
    public bool AllowMage { get; set; }
    public bool AllowThief { get; set; }

    public string? QuestCardText { get; set; }
    public string? RewardCardText { get; set; }
    public string? RewardNote { get; set; }
    public string? Notes { get; set; }
    public string? ImagePath { get; set; }
    public int XpCost { get; set; }

    public ICollection<PersonalQuestSkillReward> SkillRewards { get; set; } = [];
    public ICollection<PersonalQuestItemReward> ItemRewards { get; set; } = [];
    public ICollection<GamePersonalQuest> GameLinks { get; set; } = [];
    public ICollection<CharacterPersonalQuest> CharacterAssignments { get; set; } = [];
}
