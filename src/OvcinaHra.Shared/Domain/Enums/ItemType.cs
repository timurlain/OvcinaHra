using System.ComponentModel.DataAnnotations;

namespace OvcinaHra.Shared.Domain.Enums;

public enum ItemType
{
    [Display(Name = "Zbraň")]
    Weapon,

    [Display(Name = "Štít")]
    Shield,

    [Display(Name = "Zbroj")]
    Armor,

    [Display(Name = "Helma")]
    Helmet,

    [Display(Name = "Střelná zbraň")]
    Firearm,

    [Display(Name = "Kůň")]
    Horse,

    [Display(Name = "Ovládaný tvor")]
    ControlledCreature,

    [Display(Name = "Životy")]
    Lives,

    [Display(Name = "Svitek")]
    Scroll,

    [Display(Name = "Lektvar")]
    Potion,

    [Display(Name = "Surovina")]
    Resource,

    [Display(Name = "Peníze")]
    Money,

    [Display(Name = "Herní zvíře")]
    GameAnimal,

    [Display(Name = "Drobný artefakt")]
    MinorArtifact,

    [Display(Name = "Šperk")]
    Jewelry,

    [Display(Name = "Artefakt")]
    Artifact,

    [Display(Name = "Ingredience")]
    Ingredient,

    [Display(Name = "Komodita")]
    Commodity,

    [Display(Name = "Ostatní")]
    Miscellaneous
}
