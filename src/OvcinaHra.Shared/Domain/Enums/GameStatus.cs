using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace OvcinaHra.Shared.Domain.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GameStatus
{
    [Display(Name = "Rozpracovaná")]
    Draft,

    [Display(Name = "Aktivní")]
    Active,

    [Display(Name = "Archivovaná")]
    Archived
}
