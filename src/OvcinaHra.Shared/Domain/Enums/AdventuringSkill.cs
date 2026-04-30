using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace OvcinaHra.Shared.Domain.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AdventuringSkill
{
    [Display(Name = "Hledání magie")]
    HledaniMagie = 1,

    [Display(Name = "Prohledávání")]
    Prohledavani = 2,

    [Display(Name = "Šestý smysl")]
    SestySmysl = 3,

    [Display(Name = "Znalost bytostí")]
    ZnalostBytosti = 4,

    [Display(Name = "Lezení")]
    Lezeni = 5
}
