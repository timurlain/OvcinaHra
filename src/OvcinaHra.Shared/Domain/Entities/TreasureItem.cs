namespace OvcinaHra.Shared.Domain.Entities;

public class TreasureItem
{
    public int TreasureQuestId { get; set; }
    public int ItemId { get; set; }
    public int Count { get; set; } = 1;

    public TreasureQuest TreasureQuest { get; set; } = null!;
    public Item Item { get; set; } = null!;
}
