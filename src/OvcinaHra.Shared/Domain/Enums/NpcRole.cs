using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace OvcinaHra.Shared.Domain.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NpcRole
{
    [Display(Name = "Osudová")]
    Fate,

    [Display(Name = "Obchodník")]
    Merchant,

    [Display(Name = "Královská")]
    King,

    [Display(Name = "Nestvůra")]
    Monster,

    [Display(Name = "Příběhová")]
    Story,

    [Display(Name = "Ostatní")]
    Other
}
