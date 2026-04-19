namespace OvcinaHra.Shared.Domain.Entities;

public class PersonalQuestItemReward
{
    public int PersonalQuestId { get; set; }
    public int ItemId { get; set; }
    public int Quantity { get; set; } = 1;

    public PersonalQuest PersonalQuest { get; set; } = null!;
    public Item Item { get; set; } = null!;
}
