using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace OvcinaHra.Shared.Domain.Enums;

/// <summary>
/// Numeric rank of a monster — informs encounter difficulty, XP rewards,
/// and spell-effect targeting ("Příšera kategorie I neútočí toto kolo").
/// Five tiers only per designer decision 2026-04-24; Display uses the
/// canonical Roman numerals the spell + item effect strings rely on.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MonsterCategory
{
    [Display(Name = "I")]   Tier1 = 1,
    [Display(Name = "II")]  Tier2 = 2,
    [Display(Name = "III")] Tier3 = 3,
    [Display(Name = "IV")]  Tier4 = 4,
    [Display(Name = "V")]   Tier5 = 5,
}
