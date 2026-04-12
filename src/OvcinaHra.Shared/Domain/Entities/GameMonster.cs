namespace OvcinaHra.Shared.Domain.Entities;

public class GameMonster
{
    public int GameId { get; set; }
    public int MonsterId { get; set; }

    public Game Game { get; set; } = null!;
    public Monster Monster { get; set; } = null!;
}
