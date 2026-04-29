using System.Globalization;
using OvcinaHra.Api.Services.Pdf;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace OvcinaHra.Api.Services;

public interface IMagicBookExportService
{
    Task<MagicBookExportFile> RenderMagicBookAsync(int gameId, CancellationToken ct = default);
}

public sealed record MagicBookExportFile(byte[] Bytes, string FileName);

public sealed class MagicBookExportProblemException(string detail)
    : Exception(detail)
{
    public string Title { get; } = "Export knihy magie se nepodařil";
    public string Detail { get; } = detail;
}

public sealed class MagicBookExportService(
    IMagicBookExportPlanner planner,
    IWebHostEnvironment environment) : IMagicBookExportService
{
    private const double A4WidthPt = 595.276;
    private const double A4HeightPt = 841.89;
    private const int Dpi = 300;
    private const int A4WidthPx = 2480;
    private const int A4HeightPx = 3508;
    private const int A6WidthPx = 1240;
    private const int A6HeightPx = 1748;
    private const int SafeMarginPx = 60;
    private const int SectionGapPx = 24;
    private const int SectionPaddingPx = 18;
    private const int SectionHeaderHeightPx = 48;
    private const int SpellNameLineHeightPx = 36;
    private const int BodyLineHeightPx = 30;
    private const int SpellGapPx = 14;

    private static readonly Color Paper = Color.ParseHex("#fff8e8");
    private static readonly Color Ink = Color.ParseHex("#26180f");
    private static readonly Color Border = Color.ParseHex("#3b2b18");

    public async Task<MagicBookExportFile> RenderMagicBookAsync(int gameId, CancellationToken ct = default)
    {
        var document = await planner.BuildAsync(gameId, ct)
            ?? throw new KeyNotFoundException($"Game {gameId} was not found.");

        if (document.SpellCount == 0)
            throw new MagicBookExportProblemException("Vybraná hra nemá přiřazená žádná kouzla.");

        var fonts = LoadFonts();
        using var lowLevelPage = RenderA6Page(document.Pages[0], fonts);
        using var highLevelPage = RenderA6Page(document.Pages[1], fonts);
        var sheets = new[]
        {
            await RenderA4SheetAsync(lowLevelPage, ct),
            await RenderA4SheetAsync(highLevelPage, ct)
        };

        var pages = sheets
            .Select((sheet, index) => new PdfImagePage(
                A4WidthPt,
                A4HeightPt,
                $"q {Pt(A4WidthPt)} 0 0 {Pt(A4HeightPt)} 0 0 cm /Sheet{index + 1} Do Q\n",
                new Dictionary<string, PdfImage> { [$"Sheet{index + 1}"] = sheet }))
            .ToList();

        var pdf = SimpleImagePdfWriter.Build(pages);
        var fileName = ExportFilenameBuilder.BuildExportFilename(
            "KnihaMagie",
            document.GameName,
            includeDate: true);
        return new MagicBookExportFile(pdf, fileName);
    }

    private static Image<Rgba32> RenderA6Page(
        MagicBookPage page,
        MagicBookFonts fonts)
    {
        var image = new Image<Rgba32>(A6WidthPx, A6HeightPx, Paper);
        image.Mutate(ctx =>
        {
            ctx.Draw(Border, 4, new RectangularPolygon(18, 18, A6WidthPx - 36, A6HeightPx - 36));
            var sectionPlans = BuildSectionPlans(page, maxEffectLines: 2);
            if (TotalSectionHeight(sectionPlans) > A6HeightPx - SafeMarginPx * 2)
                sectionPlans = BuildSectionPlans(page, maxEffectLines: 1);

            var y = (float)SafeMarginPx;
            foreach (var section in sectionPlans)
                DrawSection(ctx, section, fonts, ref y);
        });
        return image;
    }

    private static List<MagicBookSectionPlan> BuildSectionPlans(MagicBookPage page, int maxEffectLines) =>
        page.Sections
            .Where(section => section.Spells.Count > 0)
            .Select(section =>
            {
                var spells = section.Spells
                    .Select(spell =>
                    {
                        var brief = string.IsNullOrWhiteSpace(spell.Effect)
                            ? spell.Description ?? string.Empty
                            : spell.Effect;
                        var lines = Wrap(brief, 70).Take(maxEffectLines).ToList();
                        var height = SpellNameLineHeightPx + lines.Count * BodyLineHeightPx + SpellGapPx;
                        return new MagicBookSpellPlan(spell, lines, height);
                    })
                    .ToList();
                var height = SectionPaddingPx * 2
                    + SectionHeaderHeightPx
                    + spells.Sum(spell => spell.Height);
                return new MagicBookSectionPlan(section, spells, height);
            })
            .ToList();

    private static float TotalSectionHeight(IReadOnlyList<MagicBookSectionPlan> sections) =>
        sections.Sum(section => section.Height) + Math.Max(0, sections.Count - 1) * SectionGapPx;

    private static void DrawSection(
        IImageProcessingContext ctx,
        MagicBookSectionPlan plan,
        MagicBookFonts fonts,
        ref float y)
    {
        var color = Color.ParseHex(plan.Section.ColorHex);
        var textColor = plan.Section.Level == 5 ? Color.White : Ink;
        var box = new RectangularPolygon(SafeMarginPx, y, A6WidthPx - SafeMarginPx * 2, plan.Height);
        ctx.Fill(color, box);
        ctx.Draw(Border, 3, box);

        var header = $"Kouzla úrovně {plan.Section.LevelRoman}";
        DrawCenteredText(
            ctx,
            header,
            fonts.Section,
            textColor,
            SafeMarginPx,
            y + SectionPaddingPx - 2,
            A6WidthPx - SafeMarginPx * 2);

        y += SectionPaddingPx + SectionHeaderHeightPx;
        foreach (var spell in plan.Spells)
            DrawSpell(ctx, spell, fonts, textColor, ref y);

        y += SectionPaddingPx + SectionGapPx;
    }

    private static void DrawSpell(
        IImageProcessingContext ctx,
        MagicBookSpellPlan plan,
        MagicBookFonts fonts,
        Color textColor,
        ref float y)
    {
        DrawText(ctx, plan.Spell.Name, fonts.SpellName, textColor, SafeMarginPx + SectionPaddingPx, y);
        y += SpellNameLineHeightPx;
        foreach (var line in plan.EffectLines)
        {
            DrawText(ctx, line, fonts.Body, textColor, SafeMarginPx + SectionPaddingPx, y);
            y += BodyLineHeightPx;
        }

        y += SpellGapPx;
    }

    private static async Task<PdfImage> RenderA4SheetAsync(Image<Rgba32> bookPage, CancellationToken ct)
    {
        using var sheet = new Image<Rgba32>(A4WidthPx, A4HeightPx, Color.White);
        var topY = (A4HeightPx - A6HeightPx * 2) / 2;
        var slots = new[]
        {
            new Point(0, topY),
            new Point(A6WidthPx, topY),
            new Point(0, topY + A6HeightPx),
            new Point(A6WidthPx, topY + A6HeightPx)
        };

        sheet.Mutate(ctx =>
        {
            foreach (var slot in slots)
            {
                ctx.DrawImage(bookPage, slot, 1f);
                ctx.Draw(Color.ParseHex("#dddddd"), 2, new RectangularPolygon(slot.X, slot.Y, A6WidthPx, A6HeightPx));
            }
        });

        await using var stream = new MemoryStream();
        await sheet.SaveAsJpegAsync(stream, new JpegEncoder { Quality = 92 }, ct);
        return new PdfImage(A4WidthPx, A4HeightPx, stream.ToArray());
    }

    private MagicBookFonts LoadFonts()
    {
        var fonts = new FontCollection();
        var fontRoot = System.IO.Path.Combine(environment.ContentRootPath, "Fonts", "Kalam");
        var regular = fonts.Add(System.IO.Path.Combine(fontRoot, "Kalam-Regular.ttf"));
        var bold = fonts.Add(System.IO.Path.Combine(fontRoot, "Kalam-Bold.ttf"));

        return new MagicBookFonts(
            Section: bold.CreateFont(34),
            SpellName: bold.CreateFont(30),
            Body: regular.CreateFont(25));
    }

    private static void DrawText(
        IImageProcessingContext ctx,
        string text,
        Font font,
        Color color,
        float x,
        float y) =>
        ctx.DrawText(text, font, color, new PointF(x, y));

    private static void DrawCenteredText(
        IImageProcessingContext ctx,
        string text,
        Font font,
        Color color,
        float x,
        float y,
        float width)
    {
        var size = TextMeasurer.MeasureSize(text, new TextOptions(font));
        DrawText(ctx, text, font, color, x + (width - size.Width) / 2, y);
    }

    private static IEnumerable<string> Wrap(string value, int maxChars)
    {
        var words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var current = string.Empty;
        foreach (var word in words)
        {
            if (current.Length == 0)
            {
                current = word;
                continue;
            }

            if (current.Length + word.Length + 1 <= maxChars)
            {
                current += " " + word;
                continue;
            }

            yield return current;
            current = word;
        }

        if (!string.IsNullOrWhiteSpace(current))
            yield return current;
    }

    private static string Pt(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private sealed record MagicBookFonts(
        Font Section,
        Font SpellName,
        Font Body);

    private sealed record MagicBookSectionPlan(
        MagicBookLevelSection Section,
        IReadOnlyList<MagicBookSpellPlan> Spells,
        float Height);

    private sealed record MagicBookSpellPlan(
        MagicBookSpell Spell,
        IReadOnlyList<string> EffectLines,
        float Height);
}
