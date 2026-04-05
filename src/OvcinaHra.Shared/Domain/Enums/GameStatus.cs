using System.ComponentModel.DataAnnotations;

namespace OvcinaHra.Shared.Domain.Enums;

public enum GameStatus
{
    [Display(Name = "Rozpracovaná")]
    Draft,

    [Display(Name = "Aktivní")]
    Active,

    [Display(Name = "Archivovaná")]
    Archived
}
