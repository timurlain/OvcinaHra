using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Shared.Domain.Entities;

/// <summary>
/// Catalog template for a spell. Mirrors the Item/Skill pattern — this is the
/// cross-game definition; per-game configuration (price override, availability)
/// lives on <see cref="GameSpell"/>.
/// Sources: rulemaster MAG-005 (scrolls) + MAG-006 (mage levels I–V).
/// </summary>
public class Spell
{
    public int Id { get; set; }

    /// <summary>Canonical Czech name (diacritics). Unique in the catalog.</summary>
    public required string Name { get; set; }

    /// <summary>0 = scroll (MAG-005), 1–5 = mage level I–V (MAG-006).</summary>
    public int Level { get; set; }

    /// <summary>Mana cost. Scrolls = 0 (one-shot, no mana).</summary>
    public int ManaCost { get; set; }

    public SpellSchool School { get; set; }

    /// <summary>True = MAG-005 scroll (one-shot, anyone can cast).</summary>
    public bool IsScroll { get; set; }

    /// <summary>True = cast as a reaction (out of turn). Only a handful of spells.</summary>
    public bool IsReaction { get; set; }

    /// <summary>True = purchasable at a building (merchant / mudrc / library).
    /// Scrolls default to false — they're found or bought as physical items, not learned.</summary>
    public bool IsLearnable { get; set; }

    /// <summary>Minimum mage level to cast. 0 for scrolls (anyone).</summary>
    public int MinMageLevel { get; set; }

    /// <summary>Learning cost in groše (MAG-007). Null for scrolls.</summary>
    public int? Price { get; set; }

    /// <summary>Mechanical card text shown to players.</summary>
    public required string Effect { get; set; }

    /// <summary>Lore / flavour. Optional.</summary>
    public string? Description { get; set; }

    public string? ImagePath { get; set; }

    public ICollection<GameSpell> GameSpells { get; set; } = [];
}
