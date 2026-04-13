namespace OvcinaHra.Shared.Domain.Entities;

public class GameEventNpc
{
    public int GameEventId { get; set; }
    public int NpcId { get; set; }
    public string? RoleInEvent { get; set; }  // optional: "attacker", "narrator", etc.

    public GameEvent GameEvent { get; set; } = null!;
    public Npc Npc { get; set; } = null!;
}
