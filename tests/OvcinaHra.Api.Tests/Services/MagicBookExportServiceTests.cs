using OvcinaHra.Api.Services;
using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Api.Tests.Services;

public class MagicBookExportServiceTests
{
    [Fact]
    public void LowLevelPageLayout_WrapsAllSpellTextWithoutTruncation()
    {
        var page = new MagicBookPage(
            PageNumber: 1,
            Title: "Úrovně I-III",
            Sections:
            [
                Section(1, "I", "#ffff00",
                [
                    "Spolubojovník má do příštího útoku bonus rovný tvému ÚČ +",
                    "\"Reakce\" po útoku nepřítele. Změň výsledek jednoho hodu na nové číslo.",
                    "Ohnivě. Zraň jeden cíl za 1k6 z ohněm.",
                    "Mentální. Cílová bytost s IÚ 2 přijde o 1 akci."
                ]),
                Section(2, "II", "#92d050",
                [
                    "Všichni na tvé straně mají toto kolo navíc 1k6+ na těžké úkoly.",
                    "Vyvolej bytost S/2 s Š. Nemůže krýt, může okamžitě jednat.",
                    "Zvyš zranění svého příštího ohnivého kouzla o 5.",
                    "Toto kolo všechny výsledky 1k6+ při střeleckých útocích přičti."
                ]),
                Section(3, "III", "#ff0000",
                [
                    "Toto kolo všechny výsledky 1k6+ při blízkých útocích přičti.",
                    "Ohnivě. Zraň až dva cíle za 1k6 z ohněm.",
                    "Mentální. Nepříteli s ≤ 7 klesnou životy na 0.",
                    "Mentální, \"reakce\" po útoku na tebe. Nepřítel útočí s nevýhodou."
                ])
            ]);
        var fontRoot = Path.Combine(FindRepoRoot(), "src", "OvcinaHra.Api", "Fonts", "Kalam");

        var diagnostics = MagicBookExportService.CalculateLayoutForTesting(page, fontRoot);

        Assert.Equal(12, diagnostics.SpellCount);
        Assert.False(diagnostics.HasTruncatedLines);
        Assert.True(diagnostics.TotalHeight <= diagnostics.AvailableHeight);
        Assert.True(diagnostics.MaxLineWidth <= diagnostics.TextWidth + 0.5f);
    }

    private static MagicBookLevelSection Section(
        int level,
        string roman,
        string color,
        IReadOnlyList<string> effects) =>
        new(
            level,
            roman,
            color,
            effects.Select((effect, index) => new MagicBookSpell(
                GameSpellId: level * 100 + index,
                SpellId: level * 100 + index,
                Name: $"Kouzlo {roman}-{index + 1}",
                Level: level,
                School: SpellSchool.Fire,
                ManaCost: level,
                MinMageLevel: level,
                EffectivePrice: level * 10,
                Effect: effect,
                Description: null,
                IsReaction: false,
                IsFindable: true,
                AvailabilityNotes: null)).ToList());

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OvcinaHra.slnx")))
            dir = dir.Parent;

        return dir?.FullName ?? throw new InvalidOperationException("Could not find repository root.");
    }
}
