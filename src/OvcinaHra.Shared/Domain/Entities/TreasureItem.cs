namespace OvcinaHra.Shared.Domain.Entities;

public class TreasureItem
{
    public int Id { get; set; }
    public int ItemId { get; set; }
    public int GameId { get; set; }
    public int Count { get; set; } = 1;
    public int? TreasureQuestId { get; set; }

    public Item Item { get; set; } = null!;
    public Game Game { get; set; } = null!;
    public TreasureQuest? TreasureQuest { get; set; }
}
