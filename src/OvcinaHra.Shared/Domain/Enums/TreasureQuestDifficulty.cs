using System.ComponentModel.DataAnnotations;

namespace OvcinaHra.Shared.Domain.Enums;

public enum TreasureQuestDifficulty
{
    [Display(Name = "Začátek")]
    Start,

    [Display(Name = "Začátek hry")]
    Early,

    [Display(Name = "Střed hry")]
    Midgame,

    [Display(Name = "Konec hry")]
    Lategame
}
