using OvcinaHra.Api.Services;

namespace OvcinaHra.Api.Tests.Services;

public class ExportFilenameBuilderTests
{
    [Fact]
    public void BuildExportFilename_UsesExportTypeGameAndOptionalDate()
    {
        var fileName = ExportFilenameBuilder.BuildExportFilename(
            "OrganizatorA4",
            "Balinova pozvánka",
            includeDate: true);

        Assert.Equal($"OrganizatorA4_Balinova-pozvanka_{DateTime.Today:yyyy-MM-dd}.pdf", fileName);
    }

    [Fact]
    public void BuildExportFilename_SanitizesDiacriticsAndUnsafeSeparators()
    {
        var fileName = ExportFilenameBuilder.BuildExportFilename(
            "SeznamKouzel",
            "Žluťoučký kůň / Černý:les");

        Assert.Equal("SeznamKouzel_Zlutoucky-kun-Cerny-les.pdf", fileName);
    }
}
