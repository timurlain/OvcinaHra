namespace OvcinaHra.Shared.Domain.Entities;

public class PersonalQuestSpellReward
{
    public int PersonalQuestId { get; set; }
    public int SpellId { get; set; }
    public int Quantity { get; set; } = 1;

    public PersonalQuest PersonalQuest { get; set; } = null!;
    public Spell Spell { get; set; } = null!;
}
