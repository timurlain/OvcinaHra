using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace OvcinaHra.Shared.Domain.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SkillCategory
{
    /// <summary>Class-restricted skill — auto-gained with class leveling.</summary>
    [Display(Name = "Povolání")]
    Class,

    /// <summary>Adventure skill — learnable by anyone through training.</summary>
    [Display(Name = "Dobrodružná")]
    Adventure,

    /// <summary>Quest skill — awarded by completing a personal quest.</summary>
    [Display(Name = "Questová")]
    Quest
}
