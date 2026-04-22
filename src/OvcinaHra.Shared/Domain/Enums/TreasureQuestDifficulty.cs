using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace OvcinaHra.Shared.Domain.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TreasureQuestDifficulty
{
    [Display(Name = "Start")]
    Start,

    [Display(Name = "Rozvoj hry")]
    Early,

    [Display(Name = "Střed hry")]
    Midgame,

    [Display(Name = "Závěr hry")]
    Lategame
}
