using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace OvcinaHra.Shared.Domain.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CipherContentType
{
    [Display(Name = "Nic")]
    Nic = 0,

    [Display(Name = "Informace")]
    Info = 1,

    [Display(Name = "Pytlík")]
    Pytlik = 2,

    [Display(Name = "Akce")]
    Akce = 3,

    [Display(Name = "Kombinace")]
    Kombinace = 4,

    [Display(Name = "Voucher")]
    Voucher = 5,

    [Display(Name = "Klíčové slovo")]
    Keyword = 6
}
