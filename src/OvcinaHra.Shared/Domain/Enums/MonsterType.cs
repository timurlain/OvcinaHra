using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace OvcinaHra.Shared.Domain.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MonsterType
{
    [Display(Name = "Nemrtvý")]
    Undead,

    [Display(Name = "Zvíře")]
    Beast,

    [Display(Name = "Skřet")]
    Goblin,

    [Display(Name = "Dobrotivý")]
    Benevolent,

    [Display(Name = "Legenda")]
    Legend,

    [Display(Name = "Ostatní")]
    Miscellaneous
}
