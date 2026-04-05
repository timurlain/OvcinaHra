using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace OvcinaHra.Shared.Domain.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
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
