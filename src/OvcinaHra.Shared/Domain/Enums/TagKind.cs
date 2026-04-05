using System.ComponentModel.DataAnnotations;

namespace OvcinaHra.Shared.Domain.Enums;

public enum TagKind
{
    [Display(Name = "Příšera")]
    Monster,

    [Display(Name = "Úkol")]
    Quest
}
