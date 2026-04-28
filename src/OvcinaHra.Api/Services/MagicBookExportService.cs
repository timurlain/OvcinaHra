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

    private static readonly Color Paper = Color.ParseHex("#fff8e8");
    private static readonly Color Ink = Color.ParseHex("#26180f");
    private static readonly Color Muted = Color.ParseHex("#6d5a45");
    private static readonly Color Border = Color.ParseHex("#3b2b18");

    public async Task<MagicBookExportFile> RenderMagicBookAsync(int gameId, CancellationToken ct = default)
    {
        var document = await planner.BuildAsync(gameId, ct)
            ?? throw new KeyNotFoundException($"Game {gameId} was not found.");

        if (document.SpellCount == 0)
            throw new MagicBookExportProblemException("Vybraná hra nemá přiřazená žádná kouzla.");

        var fonts = LoadFonts();
        using var lowLevelPage = RenderA6Page(document, document.Pages[0], fonts);
        using var highLevelPage = RenderA6Page(document, document.Pages[1], fonts);
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
        var fileName = $"{Slugify(document.GameName)}-kniha-magie-{DateTime.Today:yyyy-MM-dd}.pdf";
        return new MagicBookExportFile(pdf, fileName);
    }

    private static Image<Rgba32> RenderA6Page(
        MagicBookDocument document,
        MagicBookPage page,
        MagicBookFonts fonts)
    {
        var image = new Image<Rgba32>(A6WidthPx, A6HeightPx, Paper);
        image.Mutate(ctx =>
        {
            ctx.Draw(Border, 4, new RectangularPolygon(18, 18, A6WidthPx - 36, A6HeightPx - 36));
            DrawText(ctx, $"Kniha magie - {document.GameName}", fonts.Title, Ink, SafeMarginPx, 44);
            DrawText(ctx, page.Title, fonts.Meta, Muted, SafeMarginPx, 96);

            var y = 145f;
            foreach (var section in page.Sections)
            {
                if (section.Spells.Count == 0)
                    continue;

                DrawSectionHeader(ctx, section, fonts, ref y);
                foreach (var spell in section.Spells)
                    DrawSpell(ctx, spell, fonts, ref y, A6HeightPx - SafeMarginPx);
            }
        });
        return image;
    }

    private static void DrawSectionHeader(
        IImageProcessingContext ctx,
        MagicBookLevelSection section,
        MagicBookFonts fonts,
        ref float y)
    {
        var color = Color.ParseHex(section.ColorHex);
        ctx.Fill(color, new RectangularPolygon(SafeMarginPx, y, A6WidthPx - SafeMarginPx * 2, 38));
        ctx.Draw(Border, 2, new RectangularPolygon(SafeMarginPx, y, A6WidthPx - SafeMarginPx * 2, 38));
        DrawText(ctx, $"Úroveň {section.LevelRoman}", fonts.Section, Ink, SafeMarginPx + 14, y + 3);
        y += 52;
    }

    private static void DrawSpell(
        IImageProcessingContext ctx,
        MagicBookSpell spell,
        MagicBookFonts fonts,
        ref float y,
        float bottom)
    {
        if (y > bottom - 72)
            return;

        DrawText(ctx, spell.Name, fonts.SpellName, Ink, SafeMarginPx, y);
        var meta = BuildMetaLine(spell);
        if (!string.IsNullOrWhiteSpace(meta))
            DrawText(ctx, meta, fonts.Meta, Muted, SafeMarginPx + 420, y + 4);

        y += 29;
        foreach (var line in Wrap(spell.Effect, 88).Take(3))
        {
            DrawText(ctx, line, fonts.Body, Ink, SafeMarginPx, y);
            y += 25;
        }

        if (!string.IsNullOrWhiteSpace(spell.Description) && y < bottom - 38)
        {
            foreach (var line in Wrap(spell.Description, 96).Take(1))
            {
                DrawText(ctx, line, fonts.Small, Muted, SafeMarginPx, y);
                y += 21;
            }
        }

        if (!string.IsNullOrWhiteSpace(spell.AvailabilityNotes) && y < bottom - 38)
        {
            foreach (var line in Wrap(spell.AvailabilityNotes, 96).Take(1))
            {
                DrawText(ctx, line, fonts.Small, Muted, SafeMarginPx, y);
                y += 21;
            }
        }

        y += 12;
    }

    private static async Task<PdfImage> RenderA4SheetAsync(Image<Rgba32> bookPage, CancellationToken ct)
    {
        using var sheet = new Image<Rgba32>(A4WidthPx, A4HeightPx, Color.White);
        var x = (A4WidthPx - A6WidthPx) / 2;
        var halfHeight = A4HeightPx / 2;
        var topY = (halfHeight - A6HeightPx) / 2;
        var bottomY = halfHeight + topY;

        sheet.Mutate(ctx =>
        {
            ctx.DrawImage(bookPage, new Point(x, topY), 1f);
            ctx.DrawImage(bookPage, new Point(x, bottomY), 1f);
            ctx.Draw(Color.ParseHex("#dddddd"), 2, new RectangularPolygon(x, topY, A6WidthPx, A6HeightPx));
            ctx.Draw(Color.ParseHex("#dddddd"), 2, new RectangularPolygon(x, bottomY, A6WidthPx, A6HeightPx));
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
            Title: bold.CreateFont(38),
            Section: bold.CreateFont(28),
            SpellName: bold.CreateFont(25),
            Body: regular.CreateFont(22),
            Meta: regular.CreateFont(18),
            Small: regular.CreateFont(17));
    }

    private static void DrawText(
        IImageProcessingContext ctx,
        string text,
        Font font,
        Color color,
        float x,
        float y) =>
        ctx.DrawText(text, font, color, new PointF(x, y));

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

    private static string BuildMetaLine(MagicBookSpell spell)
    {
        var parts = new List<string> { $"mana {spell.ManaCost}" };
        if (spell.MinMageLevel > 1)
            parts.Add($"mág {LevelRoman(spell.MinMageLevel)}");
        if (spell.EffectivePrice is int price)
            parts.Add($"{price} gr");
        if (spell.IsReaction)
            parts.Add("reakce");
        if (spell.IsFindable)
            parts.Add("nález");
        return string.Join(" · ", parts);
    }

    private static string LevelRoman(int level) => level switch
    {
        1 => "I",
        2 => "II",
        3 => "III",
        4 => "IV",
        5 => "V",
        _ => level.ToString(System.Globalization.CultureInfo.InvariantCulture)
    };

    private static string Pt(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Slugify(string value)
    {
        var normalized = value.ToLowerInvariant();
        var chars = normalized.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        return string.Join('-', new string(chars)
            .Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private sealed record MagicBookFonts(
        Font Title,
        Font Section,
        Font SpellName,
        Font Body,
        Font Meta,
        Font Small);
}
