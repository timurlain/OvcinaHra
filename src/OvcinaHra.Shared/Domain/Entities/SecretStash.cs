namespace OvcinaHra.Shared.Domain.Entities;

public class SecretStash
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? ImagePath { get; set; }
    public int LocationId { get; set; }
    public int GameId { get; set; }

    public Location Location { get; set; } = null!;
    public Game Game { get; set; } = null!;
    public ICollection<TreasureQuest> TreasureQuests { get; set; } = [];
}
