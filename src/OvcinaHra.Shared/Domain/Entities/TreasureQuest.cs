using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Shared.Domain.Entities;

public class TreasureQuest
{
    public int Id { get; set; }
    public required string Title { get; set; }
    public string? Clue { get; set; }
    public GameTimePhase Difficulty { get; set; }
    public int? LocationId { get; set; }
    public int? SecretStashId { get; set; }
    public int GameId { get; set; }

    public Location? Location { get; set; }
    public SecretStash? SecretStash { get; set; }
    public Game Game { get; set; } = null!;
    public ICollection<TreasureItem> TreasureItems { get; set; } = [];
}
