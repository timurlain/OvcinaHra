using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace OvcinaHra.Shared.Domain.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PhysicalForm
{
    [Display(Name = "Karta")]
    Card,

    [Display(Name = "Papír")]
    Paper,

    [Display(Name = "Lahvička")]
    Vial,

    [Display(Name = "Mince")]
    Coin,

    [Display(Name = "Kamínek")]
    Pebble,

    [Display(Name = "Krystal")]
    Crystal,

    [Display(Name = "Čirý krystal")]
    ClearCrystal,

    [Display(Name = "Šperk")]
    Jewelry,

    [Display(Name = "Obecný předmět")]
    GenericItem
}
