using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Shared.Extensions;

public static class CipherSkillKeyExtensions
{
    public static readonly IReadOnlyList<CipherSkillKey> All =
    [
        CipherSkillKey.HledaniMagie,
        CipherSkillKey.Prohledavani,
        CipherSkillKey.SestySmysl,
        CipherSkillKey.ZnalostBytosti,
        CipherSkillKey.Lezeni
    ];

    public static string GetDisplayName(this CipherSkillKey key) => key switch
    {
        CipherSkillKey.HledaniMagie => "Hledání magie",
        CipherSkillKey.Prohledavani => "Prohledávání",
        CipherSkillKey.SestySmysl => "Šestý smysl",
        CipherSkillKey.ZnalostBytosti => "Znalost bytostí",
        CipherSkillKey.Lezeni => "Lezení",
        _ => key.ToString()
    };

    public static string GetSlug(this CipherSkillKey key) => key switch
    {
        CipherSkillKey.HledaniMagie => "hledani-magie",
        CipherSkillKey.Prohledavani => "prohledavani",
        CipherSkillKey.SestySmysl => "sesty-smysl",
        CipherSkillKey.ZnalostBytosti => "znalost-bytosti",
        CipherSkillKey.Lezeni => "lezeni",
        _ => key.ToString()
    };

    public static int GetMaxMessageLetters(this CipherSkillKey key) =>
        key == CipherSkillKey.Lezeni ? 72 : 74;

    public static bool TryParseSlug(string value, out CipherSkillKey key)
    {
        foreach (var candidate in All)
        {
            if (string.Equals(candidate.GetSlug(), value, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                key = candidate;
                return true;
            }
        }

        key = default;
        return false;
    }
}
