namespace OvcinaHra.Shared.Domain.Entities;

public class QuestReward
{
    public int QuestId { get; set; }
    public int ItemId { get; set; }
    public int Quantity { get; set; }

    public Quest Quest { get; set; } = null!;
    public Item Item { get; set; } = null!;
}
