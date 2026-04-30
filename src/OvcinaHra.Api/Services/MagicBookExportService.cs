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
    IWebHostEnvironment environment,
    ILogger<MagicBookExportService> logger) : IMagicBookExportService
{
    private const double A4WidthPt = 595.276;
    private const double A4HeightPt = 841.89;
    private const int Dpi = 300;
    private const int A4WidthPx = 2480;
    private const int A4HeightPx = 3508;
    private const int A6WidthPx = 1240;
    private const int A6HeightPx = 1748;

    private static readonly Color Paper = Color.ParseHex("#fff8e8");
    private static readonly Color SpellBoxFill = Color.ParseHex("#fffaf0");
    private static readonly Color Ink = Color.ParseHex("#26180f");
    private static readonly Color Border = Color.ParseHex("#3b2b18");
    private static readonly MagicBookLayout[] LayoutCandidates =
    [
        new(
            "comfortable",
            SafeMarginPx: 42,
            SectionGapPx: 18,
            SectionPaddingPx: 20,
            SectionHeaderHeightPx: 56,
            SpellNameLineHeightPx: 42,
            BodyLineHeightPx: 34,
            SpellGapPx: 12,
            SpellBoxPaddingX: 16,
            SpellBoxPaddingY: 12,
            SectionFontSize: 38,
            SpellNameFontSize: 34,
            BodyFontSize: 28),
        new(
            "compact",
            SafeMarginPx: 42,
            SectionGapPx: 12,
            SectionPaddingPx: 14,
            SectionHeaderHeightPx: 42,
            SpellNameLineHeightPx: 32,
            BodyLineHeightPx: 25,
            SpellGapPx: 8,
            SpellBoxPaddingX: 14,
            SpellBoxPaddingY: 8,
            SectionFontSize: 32,
            SpellNameFontSize: 27,
            BodyFontSize: 22),
        new(
            "dense",
            SafeMarginPx: 34,
            SectionGapPx: 10,
            SectionPaddingPx: 10,
            SectionHeaderHeightPx: 36,
            SpellNameLineHeightPx: 28,
            BodyLineHeightPx: 22,
            SpellGapPx: 6,
            SpellBoxPaddingX: 12,
            SpellBoxPaddingY: 7,
            SectionFontSize: 28,
            SpellNameFontSize: 24,
            BodyFontSize: 19)
    ];

    public async Task<MagicBookExportFile> RenderMagicBookAsync(int gameId, CancellationToken ct = default)
    {
        var document = await planner.BuildAsync(gameId, ct)
            ?? throw new KeyNotFoundException($"Game {gameId} was not found.");

        if (document.SpellCount == 0)
            throw new MagicBookExportProblemException("Vybraná hra nemá přiřazená žádná kouzla.");

        var fontFamilies = LoadFontFamilies();
        using var lowLevelPage = RenderA6Page(document.Pages[0], fontFamilies, logger);
        using var highLevelPage = RenderA6Page(document.Pages[1], fontFamilies, logger);
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
        MagicBookFontFamilies fontFamilies,
        ILogger<MagicBookExportService> logger)
    {
        var (layout, fonts, sectionPlans) = BuildPagePlan(page, fontFamilies);
        var totalHeight = TotalSectionHeight(sectionPlans, layout);
        var availableHeight = AvailablePageHeight(layout);
        var pageOverflowing = totalHeight > availableHeight;
        var image = new Image<Rgba32>(A6WidthPx, A6HeightPx, Paper);
        image.Mutate(ctx =>
        {
            ctx.Draw(Border, 4, new RectangularPolygon(14, 14, A6WidthPx - 28, A6HeightPx - 28));
            logger.LogDebug(
                "[export-server] magic-book.layout page={PageNumber} layout={LayoutName} totalHeight={TotalHeight} availableHeight={AvailableHeight} truncated={Truncated}",
                page.PageNumber,
                layout.Name,
                totalHeight,
                availableHeight,
                pageOverflowing);

            sectionPlans = ExpandSectionsToPageHeight(sectionPlans, layout);

            var y = (float)layout.SafeMarginPx;
            foreach (var section in sectionPlans)
                DrawSection(ctx, section, fonts, layout, pageOverflowing, logger, ref y);
        });
        return image;
    }

    internal static MagicBookLayoutDiagnostics CalculateLayoutForTesting(
        MagicBookPage page,
        string fontRoot)
    {
        var fontFamilies = LoadFontFamilies(fontRoot);
        var (layout, fonts, sectionPlans) = BuildPagePlan(page, fontFamilies);
        var textWidth = SpellTextWidth(layout);
        var maxLineWidth = sectionPlans
            .SelectMany(section => section.Spells)
            .SelectMany(spell => spell.EffectLines)
            .Select(line => TextMeasurer.MeasureSize(line, new TextOptions(fonts.Body)).Width)
            .DefaultIfEmpty(0)
            .Max();

        return new MagicBookLayoutDiagnostics(
            layout.Name,
            TotalSectionHeight(sectionPlans, layout),
            AvailablePageHeight(layout),
            textWidth,
            sectionPlans.Sum(section => section.Spells.Count),
            sectionPlans.Any(section => section.Spells.Any(spell => spell.Truncated))
                || TotalSectionHeight(sectionPlans, layout) > AvailablePageHeight(layout),
            maxLineWidth);
    }

    private static (MagicBookLayout Layout, MagicBookFonts Fonts, List<MagicBookSectionPlan> Sections)
        BuildPagePlan(MagicBookPage page, MagicBookFontFamilies fontFamilies)
    {
        MagicBookLayout? fallbackLayout = null;
        MagicBookFonts? fallbackFonts = null;
        List<MagicBookSectionPlan>? fallbackSections = null;

        foreach (var layout in LayoutCandidates)
        {
            var fonts = CreateFonts(fontFamilies, layout);
            var sections = BuildSectionPlans(page, fonts, layout);
            fallbackLayout = layout;
            fallbackFonts = fonts;
            fallbackSections = sections;
            if (TotalSectionHeight(sections, layout) <= AvailablePageHeight(layout))
                return (layout, fonts, sections);
        }

        return (fallbackLayout!, fallbackFonts!, fallbackSections!);
    }

    private static List<MagicBookSectionPlan> BuildSectionPlans(
        MagicBookPage page,
        MagicBookFonts fonts,
        MagicBookLayout layout) =>
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
                        var textWidth = SpellTextWidth(layout);
                        var lines = Wrap(brief, fonts.Body, textWidth).ToList();
                        var truncated = lines.Any(line => MeasureWidth(line, fonts.Body) > textWidth + 0.5f);
                        var height = layout.SpellBoxPaddingY * 2
                            + layout.SpellNameLineHeightPx
                            + lines.Count * layout.BodyLineHeightPx;
                        return new MagicBookSpellPlan(spell, lines, height, brief.Length, truncated);
                    })
                    .ToList();
                var height = layout.SectionPaddingPx * 2
                    + layout.SectionHeaderHeightPx
                    + spells.Sum(spell => spell.Height)
                    + Math.Max(0, spells.Count - 1) * layout.SpellGapPx;
                return new MagicBookSectionPlan(section, spells, height);
            })
            .ToList();

    private static float TotalSectionHeight(
        IReadOnlyList<MagicBookSectionPlan> sections,
        MagicBookLayout layout) =>
        sections.Sum(section => section.Height) + Math.Max(0, sections.Count - 1) * layout.SectionGapPx;

    private static float AvailablePageHeight(MagicBookLayout layout) =>
        A6HeightPx - layout.SafeMarginPx * 2;

    private static float SpellTextWidth(MagicBookLayout layout) =>
        A6WidthPx
        - layout.SafeMarginPx * 2
        - layout.SectionPaddingPx * 2
        - layout.SpellBoxPaddingX * 2;

    private static List<MagicBookSectionPlan> ExpandSectionsToPageHeight(
        IReadOnlyList<MagicBookSectionPlan> sections,
        MagicBookLayout layout)
    {
        if (sections.Count == 0)
            return [];

        var available = AvailablePageHeight(layout);
        var total = TotalSectionHeight(sections, layout);
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
        MagicBookLayout layout,
        bool pageOverflowing,
        ILogger<MagicBookExportService> logger,
        ref float y)
    {
        var color = Color.ParseHex(plan.Section.ColorHex);
        var textColor = plan.Section.Level == 5 ? Color.White : Ink;
        var boxTop = y;
        var box = new RectangularPolygon(
            layout.SafeMarginPx,
            boxTop,
            A6WidthPx - layout.SafeMarginPx * 2,
            plan.Height);
        ctx.Fill(color, box);
        ctx.Draw(Border, 3, box);

        var header = $"Kouzla úrovně {plan.Section.LevelRoman}";
        DrawCenteredText(
            ctx,
            header,
            fonts.Section,
            textColor,
            layout.SafeMarginPx,
            boxTop + layout.SectionPaddingPx - 2,
            A6WidthPx - layout.SafeMarginPx * 2);

        y = boxTop + layout.SectionPaddingPx + layout.SectionHeaderHeightPx;
        foreach (var spell in plan.Spells)
            DrawSpell(ctx, spell, fonts, layout, pageOverflowing, logger, ref y);

        y = boxTop + plan.Height + layout.SectionGapPx;
    }

    private static void DrawSpell(
        IImageProcessingContext ctx,
        MagicBookSpellPlan plan,
        MagicBookFonts fonts,
        MagicBookLayout layout,
        bool pageOverflowing,
        ILogger<MagicBookExportService> logger,
        ref float y)
    {
        var boxX = layout.SafeMarginPx + layout.SectionPaddingPx;
        var boxWidth = A6WidthPx - layout.SafeMarginPx * 2 - layout.SectionPaddingPx * 2;
        var box = new RectangularPolygon(boxX, y, boxWidth, plan.Height);
        ctx.Fill(SpellBoxFill, box);
        ctx.Draw(Border, 2, box);

        logger.LogDebug(
            "[export-server] magic-book.spell-render levelId={LevelId} spellId={SpellId} descLen={DescriptionLength} boxHeight={BoxHeight} truncated={Truncated}",
            plan.Spell.Level,
            plan.Spell.SpellId,
            plan.EffectTextLength,
            plan.Height,
            plan.Truncated || pageOverflowing);

        var textX = boxX + layout.SpellBoxPaddingX;
        var textY = y + layout.SpellBoxPaddingY - 2;
        DrawText(ctx, plan.Spell.Name, fonts.SpellName, Ink, textX, textY);
        textY += layout.SpellNameLineHeightPx;
        foreach (var line in plan.EffectLines)
        {
            DrawText(ctx, line, fonts.Body, Ink, textX, textY);
            textY += layout.BodyLineHeightPx;
        }

        y += plan.Height + layout.SpellGapPx;
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

    private MagicBookFontFamilies LoadFontFamilies()
    {
        var fontRoot = System.IO.Path.Combine(environment.ContentRootPath, "Fonts", "Kalam");
        return LoadFontFamilies(fontRoot);
    }

    private static MagicBookFontFamilies LoadFontFamilies(string fontRoot)
    {
        var fonts = new FontCollection();
        var regular = fonts.Add(System.IO.Path.Combine(fontRoot, "Kalam-Regular.ttf"));
        var bold = fonts.Add(System.IO.Path.Combine(fontRoot, "Kalam-Bold.ttf"));

        return new MagicBookFontFamilies(regular, bold);
    }

    private static MagicBookFonts CreateFonts(
        MagicBookFontFamilies families,
        MagicBookLayout layout)
    {
        return new MagicBookFonts(
            Section: families.Bold.CreateFont(layout.SectionFontSize),
            SpellName: families.Bold.CreateFont(layout.SpellNameFontSize),
            Body: families.Regular.CreateFont(layout.BodyFontSize));
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

    private static IEnumerable<string> Wrap(string value, Font font, float maxWidth)
    {
        var words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var current = string.Empty;
        foreach (var token in words.SelectMany(word => BreakLongWord(word, font, maxWidth)))
        {
            if (current.Length == 0)
            {
                current = token;
                continue;
            }

            var candidate = current + " " + token;
            if (MeasureWidth(candidate, font) <= maxWidth)
            {
                current = candidate;
                continue;
            }

            yield return current;
            current = token;
        }

        if (!string.IsNullOrWhiteSpace(current))
            yield return current;
    }

    private static IEnumerable<string> BreakLongWord(string word, Font font, float maxWidth)
    {
        if (MeasureWidth(word, font) <= maxWidth)
        {
            yield return word;
            yield break;
        }

        var current = string.Empty;
        foreach (var c in word)
        {
            var candidate = current + c;
            if (current.Length > 0 && MeasureWidth(candidate, font) > maxWidth)
            {
                yield return current;
                current = c.ToString();
                continue;
            }

            current = candidate;
        }

        if (!string.IsNullOrEmpty(current))
            yield return current;
    }

    private static float MeasureWidth(string value, Font font) =>
        TextMeasurer.MeasureSize(value, new TextOptions(font)).Width;

    private static string Pt(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private sealed record MagicBookFonts(
        Font Section,
        Font SpellName,
        Font Body);

    private sealed record MagicBookFontFamilies(
        FontFamily Regular,
        FontFamily Bold);

    private sealed record MagicBookLayout(
        string Name,
        int SafeMarginPx,
        int SectionGapPx,
        int SectionPaddingPx,
        int SectionHeaderHeightPx,
        int SpellNameLineHeightPx,
        int BodyLineHeightPx,
        int SpellGapPx,
        int SpellBoxPaddingX,
        int SpellBoxPaddingY,
        float SectionFontSize,
        float SpellNameFontSize,
        float BodyFontSize);

    private sealed record MagicBookSectionPlan(
        MagicBookLevelSection Section,
        IReadOnlyList<MagicBookSpellPlan> Spells,
        float Height);

    private sealed record MagicBookSpellPlan(
        MagicBookSpell Spell,
        IReadOnlyList<string> EffectLines,
        float Height,
        int EffectTextLength,
        bool Truncated);
}

internal sealed record MagicBookLayoutDiagnostics(
    string LayoutName,
    float TotalHeight,
    float AvailableHeight,
    float TextWidth,
    int SpellCount,
    bool HasTruncatedLines,
    float MaxLineWidth);
