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
    private const int SafeMarginPx = 42;
    private const int SectionGapPx = 18;
    private const int SectionPaddingPx = 20;
    private const int SectionHeaderHeightPx = 56;
    private const int SpellNameLineHeightPx = 42;
    private const int BodyLineHeightPx = 34;
    private const int SpellGapPx = 12;
    private const int SpellBoxPaddingX = 16;
    private const int SpellBoxPaddingY = 12;

    private static readonly Color Paper = Color.ParseHex("#fff8e8");
    private static readonly Color SpellBoxFill = Color.ParseHex("#fffaf0");
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
            ctx.Draw(Border, 4, new RectangularPolygon(14, 14, A6WidthPx - 28, A6HeightPx - 28));
            var sectionPlans = BuildSectionPlans(page, maxEffectLines: 2);
            if (TotalSectionHeight(sectionPlans) > A6HeightPx - SafeMarginPx * 2)
                sectionPlans = BuildSectionPlans(page, maxEffectLines: 1);
            sectionPlans = ExpandSectionsToPageHeight(sectionPlans);

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
                        var lines = Wrap(brief, 58).Take(maxEffectLines).ToList();
                        var height = SpellBoxPaddingY * 2
                            + SpellNameLineHeightPx
                            + lines.Count * BodyLineHeightPx;
                        return new MagicBookSpellPlan(spell, lines, height);
                    })
                    .ToList();
                var height = SectionPaddingPx * 2
                    + SectionHeaderHeightPx
                    + spells.Sum(spell => spell.Height)
                    + Math.Max(0, spells.Count - 1) * SpellGapPx;
                return new MagicBookSectionPlan(section, spells, height);
            })
            .ToList();

    private static float TotalSectionHeight(IReadOnlyList<MagicBookSectionPlan> sections) =>
        sections.Sum(section => section.Height) + Math.Max(0, sections.Count - 1) * SectionGapPx;

    private static List<MagicBookSectionPlan> ExpandSectionsToPageHeight(
        IReadOnlyList<MagicBookSectionPlan> sections)
    {
        if (sections.Count == 0)
            return [];

        var available = A6HeightPx - SafeMarginPx * 2;
        var total = TotalSectionHeight(sections);
        if (total >= available)
            return sections.ToList();

        var extraPerSection = (available - total) / sections.Count;
        return sections
            .Select(section => section with { Height = section.Height + extraPerSection })
            .ToList();
    }

    private static void DrawSection(
        IImageProcessingContext ctx,
        MagicBookSectionPlan plan,
        MagicBookFonts fonts,
        ref float y)
    {
        var color = Color.ParseHex(plan.Section.ColorHex);
        var textColor = plan.Section.Level == 5 ? Color.White : Ink;
        var boxTop = y;
        var box = new RectangularPolygon(SafeMarginPx, boxTop, A6WidthPx - SafeMarginPx * 2, plan.Height);
        ctx.Fill(color, box);
        ctx.Draw(Border, 3, box);

        var header = $"Kouzla úrovně {plan.Section.LevelRoman}";
        DrawCenteredText(
            ctx,
            header,
            fonts.Section,
            textColor,
            SafeMarginPx,
            boxTop + SectionPaddingPx - 2,
            A6WidthPx - SafeMarginPx * 2);

        y = boxTop + SectionPaddingPx + SectionHeaderHeightPx;
        foreach (var spell in plan.Spells)
            DrawSpell(ctx, spell, fonts, ref y);

        y = boxTop + plan.Height + SectionGapPx;
    }

    private static void DrawSpell(
        IImageProcessingContext ctx,
        MagicBookSpellPlan plan,
        MagicBookFonts fonts,
        ref float y)
    {
        var boxX = SafeMarginPx + SectionPaddingPx;
        var boxWidth = A6WidthPx - SafeMarginPx * 2 - SectionPaddingPx * 2;
        var box = new RectangularPolygon(boxX, y, boxWidth, plan.Height);
        ctx.Fill(SpellBoxFill, box);
        ctx.Draw(Border, 2, box);

        var textX = boxX + SpellBoxPaddingX;
        var textY = y + SpellBoxPaddingY - 2;
        DrawText(ctx, plan.Spell.Name, fonts.SpellName, Ink, textX, textY);
        textY += SpellNameLineHeightPx;
        foreach (var line in plan.EffectLines)
        {
            DrawText(ctx, line, fonts.Body, Ink, textX, textY);
            textY += BodyLineHeightPx;
        }

        y += plan.Height + SpellGapPx;
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
            Section: bold.CreateFont(38),
            SpellName: bold.CreateFont(34),
            Body: regular.CreateFont(28));
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
