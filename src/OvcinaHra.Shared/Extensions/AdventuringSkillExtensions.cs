using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Shared.Extensions;

public static class AdventuringSkillExtensions
{
    public static readonly IReadOnlyList<AdventuringSkill> All =
    [
        AdventuringSkill.HledaniMagie,
        AdventuringSkill.Prohledavani,
        AdventuringSkill.SestySmysl,
        AdventuringSkill.ZnalostBytosti,
        AdventuringSkill.Lezeni
    ];

    public static string GetDisplayName(this AdventuringSkill skill) => skill switch
    {
        AdventuringSkill.HledaniMagie => "Hledání magie",
        AdventuringSkill.Prohledavani => "Prohledávání",
        AdventuringSkill.SestySmysl => "Šestý smysl",
        AdventuringSkill.ZnalostBytosti => "Znalost bytostí",
        AdventuringSkill.Lezeni => "Lezení",
        _ => skill.ToString()
    };

    public static string GetSlug(this AdventuringSkill skill) => skill switch
    {
        AdventuringSkill.HledaniMagie => "hledani-magie",
        AdventuringSkill.Prohledavani => "prohledavani",
        AdventuringSkill.SestySmysl => "sesty-smysl",
        AdventuringSkill.ZnalostBytosti => "znalost-bytosti",
        AdventuringSkill.Lezeni => "lezeni",
        _ => skill.ToString()
    };

    public static int GetMaxCipherLetters(this AdventuringSkill skill) =>
        skill == AdventuringSkill.Lezeni ? 72 : 74;

    public static bool TryParseSlug(string value, out AdventuringSkill skill)
    {
        foreach (var candidate in All)
        {
            if (string.Equals(candidate.GetSlug(), value, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                skill = candidate;
                return true;
            }
        }

        skill = default;
        return false;
    }
}
