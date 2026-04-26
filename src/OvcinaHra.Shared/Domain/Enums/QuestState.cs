using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace OvcinaHra.Shared.Domain.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum QuestState
{
    [Display(Name = "Neaktivní")] Inactive,
    [Display(Name = "Aktivní")]   Active,
    [Display(Name = "Dokončený")] Completed
}
