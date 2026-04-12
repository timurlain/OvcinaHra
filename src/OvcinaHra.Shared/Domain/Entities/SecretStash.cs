namespace OvcinaHra.Shared.Domain.Entities;

public class SecretStash
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? ImagePath { get; set; }

    public ICollection<GameSecretStash> GameSecretStashes { get; set; } = [];
    public ICollection<TreasureQuest> TreasureQuests { get; set; } = [];
}
