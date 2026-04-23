using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Shared.Extensions;

public static class RaceExtensions
{
    public static Race? TryParseRace(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var normalized = value.Trim().ToLowerInvariant();

        return normalized switch
        {
            "human" or "člověk" or "clovek" => Race.Human,
            "dwarf" or "trpaslík" or "trpaslik" => Race.Dwarf,
            "elf" => Race.Elf,
            "hobbit" or "hobit" => Race.Hobbit,
            "dunedain" or "dúnadan" or "dunadan" => Race.Dunedain,
            _ => null
        };
    }
}
