using System.ComponentModel.DataAnnotations;

namespace OvcinaHra.Shared.Domain.Enums;

public enum MonsterType
{
    [Display(Name = "Nemrtvý")]
    Undead,

    [Display(Name = "Bestie")]
    Beast,

    [Display(Name = "Goblin")]
    Goblin,

    [Display(Name = "Dobrotivý")]
    Benevolent,

    [Display(Name = "Legenda")]
    Legend,

    [Display(Name = "Ostatní")]
    Miscellaneous
}
