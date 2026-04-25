using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace OvcinaHra.Shared.Domain.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GameEventKind
{
    [Display(Name = "Úkol")]
    Quest,

    [Display(Name = "Setkání")]
    Encounter,

    [Display(Name = "Jiné")]
    Other
}
