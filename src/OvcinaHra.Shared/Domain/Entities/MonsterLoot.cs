namespace OvcinaHra.Shared.Domain.Entities;

public class MonsterLoot
{
    public int MonsterId { get; set; }
    public int ItemId { get; set; }
    public int GameId { get; set; }
    public int Quantity { get; set; }

    public Monster Monster { get; set; } = null!;
    public Item Item { get; set; } = null!;
    public Game Game { get; set; } = null!;
}
