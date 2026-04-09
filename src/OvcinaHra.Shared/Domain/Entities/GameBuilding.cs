namespace OvcinaHra.Shared.Domain.Entities;

public class GameBuilding
{
    public int GameId { get; set; }
    public int BuildingId { get; set; }

    public Game Game { get; set; } = null!;
    public Building Building { get; set; } = null!;
}
