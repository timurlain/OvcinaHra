namespace OvcinaHra.Shared.Domain.Entities;

public class GameEventLocation
{
    public int GameEventId { get; set; }
    public int LocationId { get; set; }

    public GameEvent GameEvent { get; set; } = null!;
    public Location Location { get; set; } = null!;
}
