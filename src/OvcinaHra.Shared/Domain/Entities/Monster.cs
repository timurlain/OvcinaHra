using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Domain.ValueObjects;

namespace OvcinaHra.Shared.Domain.Entities;

public class Monster
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public int Category { get; set; }
    public MonsterType MonsterType { get; set; }
    public string? Abilities { get; set; }
    public string? AiBehavior { get; set; }
    public CombatStats Stats { get; set; } = null!;
    public int? RewardXp { get; set; }
    public int? RewardMoney { get; set; }
    public string? RewardNotes { get; set; }
    public string? ImagePath { get; set; }

    public ICollection<MonsterTagLink> MonsterTags { get; set; } = [];
    public ICollection<MonsterLoot> MonsterLoots { get; set; } = [];
    public ICollection<QuestEncounter> QuestEncounters { get; set; } = [];
}
