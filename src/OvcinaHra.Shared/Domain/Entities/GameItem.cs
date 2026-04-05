namespace OvcinaHra.Shared.Domain.Entities;

public class GameItem
{
    public int GameId { get; set; }
    public int ItemId { get; set; }
    public int? Price { get; set; }
    public int? StockCount { get; set; }
    public bool IsSold { get; set; }
    public string? SaleCondition { get; set; }
    public bool IsFindable { get; set; }

    public Game Game { get; set; } = null!;
    public Item Item { get; set; } = null!;
}
