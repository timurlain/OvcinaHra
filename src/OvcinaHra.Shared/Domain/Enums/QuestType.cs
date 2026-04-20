using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace OvcinaHra.Shared.Domain.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
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
    Timed,

    [Display(Name = "Lokační")]
    Location
}
