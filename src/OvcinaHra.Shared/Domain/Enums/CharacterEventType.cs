using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace OvcinaHra.Shared.Domain.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CharacterEventType
{
    [Display(Name = "Zvýšení úrovně")]
    LevelUp,

    [Display(Name = "Získání dovednosti")]
    SkillGained,

    [Display(Name = "Změna bodů")]
    PointsChanged,

    [Display(Name = "Poznámka")]
    Note,

    [Display(Name = "Volba povolání")]
    ClassChosen
}
