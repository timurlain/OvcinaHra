using System.ComponentModel.DataAnnotations;

namespace OvcinaHra.Shared.Domain.Enums;

public enum LocationKind
{
    [Display(Name = "Město")]
    Town,

    [Display(Name = "Vesnice")]
    Village,

    [Display(Name = "Magická lokace")]
    Magical,

    [Display(Name = "Hobití lokace")]
    Hobbit,

    [Display(Name = "Divočina")]
    Wilderness,

    [Display(Name = "Dungeon")]
    Dungeon,

    [Display(Name = "Zajímavé místo")]
    PointOfInterest
}
