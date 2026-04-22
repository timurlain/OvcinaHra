using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace OvcinaHra.Shared.Domain.Enums;

/// <summary>
/// Spell school / element category from rulemaster MAG-012.
/// Includes all classical elements plus non-elemental categories — some reserved
/// values have no spells in the current catalog but are kept for future content.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SpellSchool
{
    [Display(Name = "Oheň")]        Fire,
    [Display(Name = "Mráz")]        Frost,
    [Display(Name = "Voda")]        Water,
    [Display(Name = "Země")]        Earth,
    [Display(Name = "Vítr")]        Wind,
    [Display(Name = "Jed")]         Poison,
    [Display(Name = "Mentální")]    Mental,
    [Display(Name = "Podpůrné")]    Support,
    [Display(Name = "Praktické")]   Utility
}
