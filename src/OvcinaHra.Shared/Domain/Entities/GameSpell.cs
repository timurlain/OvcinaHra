namespace OvcinaHra.Shared.Domain.Entities;

/// <summary>
/// Per-game spell configuration. Mirrors GameItem — first-class entity with
/// surrogate Id (like GameSkill), per-game overrides of the catalog defaults.
/// </summary>
public class GameSpell
{
    public int Id { get; set; }

    public int GameId { get; set; }
    public int SpellId { get; set; }

    /// <summary>Per-game override of Spell.Price (learning cost in groše). Null = inherit from catalog.</summary>
    public int? Price { get; set; }

    /// <summary>True = can be found / dropped as loot / scroll in this game.</summary>
    public bool IsFindable { get; set; }

    public string? AvailabilityNotes { get; set; }

    public Game Game { get; set; } = null!;
    public Spell Spell { get; set; } = null!;
}
