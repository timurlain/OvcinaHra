using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Shared.Domain.Entities;

public class GameMapOverlay
{
    public int GameId { get; set; }
    public MapOverlayAudience Audience { get; set; }
    public required string OverlayJson { get; set; }

    public Game Game { get; set; } = null!;
}
