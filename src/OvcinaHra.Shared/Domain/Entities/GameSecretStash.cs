namespace OvcinaHra.Shared.Domain.Entities;

public class GameSecretStash
{
    public int GameId { get; set; }
    public int SecretStashId { get; set; }
    public int LocationId { get; set; }

    public Game Game { get; set; } = null!;
    public SecretStash SecretStash { get; set; } = null!;
    public Location Location { get; set; } = null!;
}
