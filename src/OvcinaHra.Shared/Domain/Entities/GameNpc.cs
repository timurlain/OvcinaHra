namespace OvcinaHra.Shared.Domain.Entities;

public class GameNpc
{
    public int GameId { get; set; }
    public int NpcId { get; set; }

    // Player assignment — nullable for manual entry
    // PersonId is canonical (from registrace), email/name are display helpers
    public int? PlayedByPersonId { get; set; }
    public string? PlayedByName { get; set; }
    public string? PlayedByEmail { get; set; }

    public string? Notes { get; set; }

    public Game Game { get; set; } = null!;
    public Npc Npc { get; set; } = null!;
}
