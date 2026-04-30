using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Shared.Domain.Rules;

/// <summary>
/// Canonical Ovčina LARP leveling rules. Single source of truth for client-side
/// XP costs, auto-skill grants, and L5 mastery options. The API only stores
/// events (LevelUp count = level, ClassChosen sets CharacterAssignment.Class,
/// SkillGained appends to skills[]) — it doesn't know about XP costs or
/// per-class progression. All rules below are sourced from ZAK-007 (Vývoj
/// postavy) + ZAK-003/4/5/6 (per-class).
///
/// Ported from Glejt.App.Models.LevelingRules (glejt-ovcina-cz) — verbatim
/// values, adapted to OvcinaHra.Shared.Domain.Enums.PlayerClass.
/// </summary>
public static class LevelingRules
{
    /// <summary>Maximum reachable level via +1 úroveň. Beyond this, only mastery purchases.</summary>
    public const int MaxLevel = 5;

    /// <summary>Cost in experience gems (zk) for each mastery skill purchase at level 5.</summary>
    public const int MasterySkillCost = 40;

    /// <summary>
    /// Per ZAK-007 FAQ: each class can buy at most 2 of its 40-zk mastery options.
    /// Auto-granted L5 skills (Tank, Rychlé šípy, Rychlé schování, Kluzký) don't count
    /// toward this cap.
    /// </summary>
    public const int MaxBuyableMasteries = 2;

    /// <summary>
    /// Experience cost to advance from <paramref name="currentLevel"/> to currentLevel+1.
    /// Returns -1 when the character is already at or above MaxLevel.
    /// Per ZAK-007: 0→1=5, 1→2=10, 2→3=15, 3→4=20, 4→5=25.
    /// </summary>
    public static int XpCostForLevelUp(int currentLevel) => currentLevel switch
    {
        0 => 5,
        1 => 10,
        2 => 15,
        3 => 20,
        4 => 25,
        _ => -1
    };

    /// <summary>
    /// Skills that are auto-granted (free, no XP cost) when a Hrdina of this class
    /// reaches the given level. The Sken page emits a SkillGained event for each
    /// after the LevelUp event lands. Empty list = no skill gained at this level.
    /// </summary>
    public static IReadOnlyList<MidLevelSkill> AutoSkillsAtLevel(PlayerClass cls, int newLevel) =>
        (cls, newLevel) switch
        {
            // Thief gets always-on hiding from the start of combat at L2 (ZAK-005)
            (PlayerClass.Thief, 2) => new[]
            {
                new MidLevelSkill("Schování od začátku souboje",
                    "Začínáš souboj vždy schovaný (bez akce).")
            },
            // Level-5 automatic mastery skills (ZAK-003/4/5/6 — Dovednosti tabulky)
            (PlayerClass.Warrior, 5) => new[]
            {
                new MidLevelSkill("Tank", "Kryješ až 2 postavy současně místo 1.")
            },
            (PlayerClass.Archer, 5) => new[]
            {
                new MidLevelSkill("Rychlé šípy",
                    "Střelecký útok na 2 bytosti jedním hodem kostkou.")
            },
            (PlayerClass.Thief, 5) => new[]
            {
                new MidLevelSkill("Rychlé schování",
                    "Schovej se po každé akci nepřítele."),
                new MidLevelSkill("Kluzký",
                    "Při útěku ze souboje nepřicházíš o životy ani předměty.")
            },
            // Mage has no auto-granted skills (ZAK-006). Buyable masteries only.
            _ => Array.Empty<MidLevelSkill>()
        };

    /// <summary>
    /// 40-zk mastery skills available at level 5. Hrdina can buy up to MaxBuyableMasteries
    /// of these per character.
    /// </summary>
    public static IReadOnlyList<MasteryOption> BuyableMasteriesAtL5(PlayerClass cls) => cls switch
    {
        PlayerClass.Warrior => new[]
        {
            new MasteryOption("Berserk", "Použij v předkole. Toto kolo +5 ÚČ / −7 OČ."),
            new MasteryOption("Houževnatý", "Navíc +10 duší (celkem 40 na 5. úrovni).")
        },
        PlayerClass.Archer => new[]
        {
            new MasteryOption("Trickshot",
                "Střelnou zbraní proveď akci Pomoc hráči s blízkou zbraní."),
            new MasteryOption("Průraz",
                "Pokud útok zabije nepřítele, okamžitě zaútoč na dalšího.")
        },
        PlayerClass.Thief => new[]
        {
            new MasteryOption("Nenápadný", "Nepočítáš se do limitu 4 postav v souboji."),
            new MasteryOption("Jako stín", "Přepadení 4k6+. Ignoruješ krytí.")
        },
        PlayerClass.Mage => new[]
        {
            new MasteryOption("Zřídlo many", "+2 mana k zásobě (celkem 11)."),
            new MasteryOption("Čistá hlava",
                "Koncentrace vrátí VEŠKEROU odhozenou manu (bez limitu úrovně).")
        },
        _ => Array.Empty<MasteryOption>()
    };
}

public sealed record MidLevelSkill(string Name, string Description);

public sealed record MasteryOption(string Name, string Description);
