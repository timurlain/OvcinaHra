using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace OvcinaHra.Shared.Domain.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PlayerClass
{
    [Display(Name = "Válečník")]
    Warrior,

    [Display(Name = "Střelec")]
    Archer,

    [Display(Name = "Zloděj")]
    Thief,

    [Display(Name = "Mág")]
    Mage
}
