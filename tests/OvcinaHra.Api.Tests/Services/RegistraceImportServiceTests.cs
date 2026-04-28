using OvcinaHra.Api.Services;
using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Api.Tests.Services;

public class RegistraceImportServiceTests
{
    [Theory]
    [InlineData("Aradhryand", Race.Elf)]
    [InlineData("Azanulinbar-Dum", Race.Dwarf)]
    [InlineData("Azanulinbar – Dum", Race.Dwarf)]
    [InlineData("Azanulinbar—Dum", Race.Dwarf)]
    [InlineData("Esgaroth", Race.Human)]
    [InlineData("Nový Arnor", null)]
    [InlineData("Novy Arnor", null)]
    [InlineData("Novy-Arnor", null)]
    [InlineData(null, null)]
    public void InferRaceFromKingdomName_UsesCanonicalKingdomMapping(
        string? kingdomName,
        Race? expected)
    {
        Assert.Equal(expected, RegistraceImportService.InferRaceFromKingdomName(kingdomName));
    }

    [Theory]
    [InlineData("Nový Arnor", "novy arnor", "novy-arnor")]
    [InlineData("Novy-Arnor", "novy-arnor", "novy arnor")]
    public void GetKingdomLookupKeys_AliasesRegistraceNovyArnor(
        string kingdomName,
        string first,
        string second)
    {
        Assert.Equal([first, second], RegistraceImportService.GetKingdomLookupKeys(kingdomName));
    }
}
