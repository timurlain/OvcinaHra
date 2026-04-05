using System.ComponentModel.DataAnnotations;

namespace OvcinaHra.Shared.Domain.Enums;

public enum QuestType
{
    [Display(Name = "Obecný")]
    General,

    [Display(Name = "Zkažený")]
    Corrupted,

    [Display(Name = "Legendární čin")]
    LegendaryDeed,

    [Display(Name = "Pokání")]
    Penance,

    [Display(Name = "Časový")]
    Timed
}
