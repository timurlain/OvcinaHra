using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace OvcinaHra.Shared.Domain.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TagKind
{
    [Display(Name = "Příšera")]
    Monster,

    [Display(Name = "Úkol")]
    Quest
}
