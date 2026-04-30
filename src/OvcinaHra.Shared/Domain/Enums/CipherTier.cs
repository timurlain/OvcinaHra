using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace OvcinaHra.Shared.Domain.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CipherTier
{
    [Display(Name = "Prázdné")]
    Empty = 0,

    [Display(Name = "Mikro")]
    Micro = 1,

    [Display(Name = "Navázané na quest")]
    QuestTied = 2,

    [Display(Name = "Knihovní voucher")]
    StandardVoucher = 3,

    [Display(Name = "Vlajkový párovaný")]
    FlagshipPaired = 4
}
