using System.ComponentModel.DataAnnotations;

namespace OvcinaHra.Shared.Domain.Enums;

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
