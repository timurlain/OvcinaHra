namespace OvcinaHra.Shared.Domain.Entities;

public class GameLocation
{
    public int GameId { get; set; }
    public int LocationId { get; set; }

    public Game Game { get; set; } = null!;
    public Location Location { get; set; } = null!;
}
