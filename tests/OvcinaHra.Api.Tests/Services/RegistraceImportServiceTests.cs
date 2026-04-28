using OvcinaHra.Api.Services;
using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Api.Tests.Services;

public class RegistraceImportServiceTests
{
    [Theory]
    [InlineData("Aradhryand", Race.Elf)]
    [InlineData("Azanulinbar-Dum", Race.Dwarf)]
    [InlineData("Esgaroth", Race.Human)]
    [InlineData("Nový Arnor", null)]
    [InlineData("Novy Arnor", null)]
    [InlineData(null, null)]
    public void InferRaceFromKingdomName_UsesCanonicalKingdomMapping(
        string? kingdomName,
        Race? expected)
    {
        Assert.Equal(expected, RegistraceImportService.InferRaceFromKingdomName(kingdomName));
    }
}
