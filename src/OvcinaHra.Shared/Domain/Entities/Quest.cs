using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Shared.Domain.Entities;

public class Quest
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public QuestType QuestType { get; set; }
    public string? Description { get; set; }
    public string? FullText { get; set; }
    public string? TimeSlot { get; set; }
    public int? RewardXp { get; set; }
    public int? RewardMoney { get; set; }
    public string? RewardNotes { get; set; }
    public int? ChainOrder { get; set; }
    public int? ParentQuestId { get; set; }
    public int? GameId { get; set; }

    public Quest? ParentQuest { get; set; }
    public Game? Game { get; set; }
    public ICollection<Quest> ChildQuests { get; set; } = [];
    public ICollection<QuestTagLink> QuestTags { get; set; } = [];
    public ICollection<QuestLocationLink> QuestLocations { get; set; } = [];
    public ICollection<QuestEncounter> QuestEncounters { get; set; } = [];
    public ICollection<QuestReward> QuestRewards { get; set; } = [];
}
