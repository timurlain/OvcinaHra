using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace OvcinaHra.Shared.Domain.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Race
{
    [Display(Name = "Člověk")]
    Human,

    [Display(Name = "Trpaslík")]
    Dwarf,

    [Display(Name = "Elf")]
    Elf,

    [Display(Name = "Hobit")]
    Hobbit,

    [Display(Name = "Dúnadan")]
    Dunedain
}
