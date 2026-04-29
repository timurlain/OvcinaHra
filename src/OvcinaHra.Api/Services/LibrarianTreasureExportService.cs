using System.Diagnostics;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Services.Pdf;
using OvcinaHra.Shared.Domain.Enums;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace OvcinaHra.Api.Services;

public interface ILibrarianTreasureExportService
{
    Task<LibrarianTreasureExportFile> RenderLibrarianTreasuresAsync(int gameId, CancellationToken ct = default);
}

public sealed record LibrarianTreasureExportFile(byte[] Bytes, string FileName);

public sealed class LibrarianTreasureExportProblemException(string detail)
    : Exception(detail)
{
    public string Title { get; } = "Export pokladů pro knihovníka se nepodařil";
    public string Detail { get; } = detail;
}

public sealed class LibrarianTreasureExportService(
    WorldDbContext db,
    IWebHostEnvironment environment,
    ILogger<LibrarianTreasureExportService> logger) : ILibrarianTreasureExportService
{
    private const double A4WidthPt = 595.276;
    private const double A4HeightPt = 841.89;
    private const int A4WidthPx = 2480;
    private const int A4HeightPx = 3508;
    private const int MarginPx = 70;
    private const int HeaderHeightPx = 220;
    private const int TableHeaderHeightPx = 72;
    private const int FooterHeightPx = 44;
    private const int ColumnGapPx = 46;
    private const int StripHeightPx = 230;
    private const int Columns = 2;
    private const int BorderThicknessPx = 5;
    private const int CellsPerStrip = 4;
    private const float BodyFontPx = 30;
    private const float HeaderFontPx = 24;
    private const float PhaseFontPx = 32;

    private static readonly Color Paper = Color.White;
    private static readonly Color Ink = Color.Black;
    private static readonly Color MutedInk = Color.ParseHex("#333333");
    private static readonly Color HeaderFill = Color.ParseHex("#f1f1f1");
    private static readonly StringComparer CzechComparer =
        StringComparer.Create(CultureInfo.GetCultureInfo("cs-CZ"), ignoreCase: false);
    public async Task<LibrarianTreasureExportFile> RenderLibrarianTreasuresAsync(
        int gameId,
        CancellationToken ct = default)
    {
        var totalStopwatch = Stopwatch.StartNew();
        var game = await db.Games
            .AsNoTracking()
            .Where(g => g.Id == gameId)
            .Select(g => new { g.Name })
            .FirstOrDefaultAsync(ct);

        if (game is null)
            throw new KeyNotFoundException($"Game {gameId} was not found.");

        var queryStopwatch = Stopwatch.StartNew();
        var treasures = await db.TreasureQuests
            .AsNoTracking()
            .Where(t => t.GameId == gameId && t.TreasureItems.Any())
            .Include(t => t.Location)
            .Include(t => t.SecretStash)
            .Include(t => t.TreasureItems)
                .ThenInclude(ti => ti.Item)
            .ToListAsync(ct);

        var rows = treasures
            .Select(t => new LibrarianTreasureRow(
                Contents: FormatContents(t.TreasureItems
                    .OrderBy(ti => ti.Item.Name, CzechComparer)
                    .Select(ti => new LibrarianTreasureItem(ti.Item.Name, ti.Count))
                    .ToList()),
                Place: t.SecretStash?.Name ?? t.Location?.Name ?? "Bez umístění",
                Phase: PhaseLabel(t.Difficulty),
                PhaseOrder: PhaseOrder(t.Difficulty)))
            .OrderBy(r => r.PhaseOrder)
            .ThenBy(r => r.Place, CzechComparer)
            .ThenBy(r => r.Contents, CzechComparer)
            .ToList();

        logger.LogInformation(
            "[export-server] librarian-treasures.query treasureCount={TreasureCount} elapsedMs={ElapsedMs}",
            rows.Count,
            queryStopwatch.ElapsedMilliseconds);

        if (rows.Count == 0)
            throw new LibrarianTreasureExportProblemException("Vybraná hra nemá žádné skryté poklady s předměty.");

        var renderStopwatch = Stopwatch.StartNew();
        var layout = CalculateLayout(rows.Count);
        var pageImages = new List<PdfImagePage>(layout.PageCount);
        for (var pageIndex = 0; pageIndex < layout.PageCount; pageIndex++)
        {
            var pdfImage = await RenderPageAsync(game.Name, rows, layout, pageIndex, ct);
            pageImages.Add(new PdfImagePage(
                A4WidthPt,
                A4HeightPt,
                $"q {Pt(A4WidthPt)} 0 0 {Pt(A4HeightPt)} 0 0 cm /LibrarianTreasuresPage{pageIndex} Do Q\n",
                new Dictionary<string, PdfImage> { [$"LibrarianTreasuresPage{pageIndex}"] = pdfImage }));
        }

        var pdf = SimpleImagePdfWriter.Build(pageImages);

        logger.LogInformation(
            "[export-server] librarian-treasures.render pages={Pages} strips={Strips} elapsedMs={ElapsedMs}",
            layout.PageCount,
            rows.Count,
            renderStopwatch.ElapsedMilliseconds);

        var fileName = ExportFilenameBuilder.BuildExportFilename("Knihovnik", game.Name, includeDate: true);
        logger.LogInformation(
            "[export-server] librarian-treasures.exit status=200 elapsedMs={ElapsedMs}",
            totalStopwatch.ElapsedMilliseconds);
        return new LibrarianTreasureExportFile(pdf, fileName);
    }

    internal static LibrarianTreasureLayoutForTesting CalculateLayoutForTesting(int stripCount)
    {
        var layout = CalculateLayout(stripCount);
        return new LibrarianTreasureLayoutForTesting(
            layout.Columns,
            layout.RowsPerColumn,
            layout.PageCount,
            layout.StripHeightPx,
            layout.BorderThicknessPx,
            layout.CellsPerStrip);
    }

    private static LibrarianTreasureLayout CalculateLayout(int stripCount)
    {
        var rowsPerColumn = Math.Max(1,
            (A4HeightPx - MarginPx * 2 - HeaderHeightPx - TableHeaderHeightPx - FooterHeightPx) / StripHeightPx);
        var pageCapacity = rowsPerColumn * Columns;
        var pageCount = Math.Max(1, (int)Math.Ceiling(stripCount / (double)pageCapacity));
        return new LibrarianTreasureLayout(
            Columns,
            rowsPerColumn,
            pageCount,
            StripHeightPx,
            BorderThicknessPx,
            CellsPerStrip);
    }

    private async Task<PdfImage> RenderPageAsync(
        string gameName,
        IReadOnlyList<LibrarianTreasureRow> rows,
        LibrarianTreasureLayout layout,
        int pageIndex,
        CancellationToken ct)
    {
        var fonts = LoadFonts();
        using var image = new Image<Rgba32>(A4WidthPx, A4HeightPx, Paper);
        image.Mutate(ctx =>
        {
            DrawHeader(ctx, gameName, pageIndex + 1, layout.PageCount, fonts);
            DrawTable(ctx, rows, layout, pageIndex, fonts);
        });

        await using var stream = new MemoryStream();
        await image.SaveAsJpegAsync(stream, new JpegEncoder { Quality = 95 }, ct);
        return new PdfImage(A4WidthPx, A4HeightPx, stream.ToArray());
    }

    private static void DrawHeader(
        IImageProcessingContext ctx,
        string gameName,
        int pageNumber,
        int pageCount,
        LibrarianTreasureFonts fonts)
    {
        DrawCenteredText(ctx, "Knihovník - poklady", fonts.Title, Ink, MarginPx, 44, A4WidthPx - MarginPx * 2);
        DrawCenteredText(ctx, gameName, fonts.Subtitle, MutedInk, MarginPx, 112, A4WidthPx - MarginPx * 2);
        if (pageCount > 1)
        {
            DrawCenteredText(
                ctx,
                $"Strana {pageNumber}/{pageCount}",
                fonts.Small,
                MutedInk,
                MarginPx,
                164,
                A4WidthPx - MarginPx * 2);
        }
    }

    private static void DrawTable(
        IImageProcessingContext ctx,
        IReadOnlyList<LibrarianTreasureRow> rows,
        LibrarianTreasureLayout layout,
        int pageIndex,
        LibrarianTreasureFonts fonts)
    {
        var tableTop = MarginPx + HeaderHeightPx;
        var rowTop = tableTop + TableHeaderHeightPx;
        var columnWidth = (A4WidthPx - MarginPx * 2 - ColumnGapPx) / (float)Columns;
        var pageStart = pageIndex * layout.RowsPerColumn * layout.Columns;

        for (var column = 0; column < Columns; column++)
        {
            var x = MarginPx + column * (columnWidth + ColumnGapPx);
            DrawColumnHeader(ctx, x, tableTop, columnWidth, fonts);

            for (var row = 0; row < layout.RowsPerColumn; row++)
            {
                var itemIndex = pageStart + column * layout.RowsPerColumn + row;
                if (itemIndex >= rows.Count)
                    return;

                var y = rowTop + row * StripHeightPx;
                DrawTreasureStrip(ctx, rows[itemIndex], x, y, columnWidth, fonts);
            }
        }
    }

    private static void DrawColumnHeader(
        IImageProcessingContext ctx,
        float x,
        float y,
        float columnWidth,
        LibrarianTreasureFonts fonts)
    {
        var widths = CellWidths(columnWidth);
        var labels = new[] { "Obsah pokladu", "Skrýše / Lokace", "Fáze", "Razítko" };
        var cellX = x;
        for (var i = 0; i < labels.Length; i++)
        {
            var rect = new RectangularPolygon(cellX, y, widths[i], TableHeaderHeightPx);
            ctx.Fill(HeaderFill, rect);
            ctx.Draw(Ink, BorderThicknessPx, rect);
            DrawCenteredText(ctx, labels[i], fonts.Header, Ink, cellX + 10, y + 20, widths[i] - 20);
            cellX += widths[i];
        }
    }

    private static void DrawTreasureStrip(
        IImageProcessingContext ctx,
        LibrarianTreasureRow row,
        float x,
        float y,
        float columnWidth,
        LibrarianTreasureFonts fonts)
    {
        var widths = CellWidths(columnWidth);
        var cellX = x;
        var values = new[]
        {
            (Text: row.Contents, Font: fonts.Body, Lines: 5, Center: false),
            (Text: row.Place, Font: fonts.Body, Lines: 4, Center: false),
            (Text: row.Phase, Font: fonts.Phase, Lines: 1, Center: true),
            (Text: string.Empty, Font: fonts.Body, Lines: 1, Center: true)
        };

        for (var i = 0; i < values.Length; i++)
        {
            var rect = new RectangularPolygon(cellX, y, widths[i], StripHeightPx);
            ctx.Fill(Paper, rect);
            ctx.Draw(Ink, BorderThicknessPx, rect);
            if (!string.IsNullOrWhiteSpace(values[i].Text))
            {
                DrawWrappedText(
                    ctx,
                    values[i].Text,
                    values[i].Font,
                    Ink,
                    cellX + 18,
                    y + 16,
                    widths[i] - 36,
                    StripHeightPx - 32,
                    values[i].Lines,
                    values[i].Center);
            }

            cellX += widths[i];
        }
    }

    private static float[] CellWidths(float columnWidth)
    {
        var content = MathF.Round(columnWidth * 0.42f);
        var place = MathF.Round(columnWidth * 0.25f);
        var phase = MathF.Round(columnWidth * 0.13f);
        var stamp = columnWidth - content - place - phase;
        return [content, place, phase, stamp];
    }

    private LibrarianTreasureFonts LoadFonts()
    {
        var fontRoot = System.IO.Path.Combine(environment.ContentRootPath, "Fonts", "Inter");
        var fontCollection = new FontCollection();
        var regular = fontCollection.Add(System.IO.Path.Combine(fontRoot, "Inter-Regular.ttf"));
        var bold = fontCollection.Add(System.IO.Path.Combine(fontRoot, "Inter-Bold.ttf"));
        return new LibrarianTreasureFonts(
            Title: bold.CreateFont(52),
            Subtitle: regular.CreateFont(31),
            Header: bold.CreateFont(HeaderFontPx),
            Body: regular.CreateFont(BodyFontPx),
            Phase: bold.CreateFont(PhaseFontPx),
            Small: regular.CreateFont(24));
    }

    private static string FormatContents(IReadOnlyList<LibrarianTreasureItem> items) =>
        string.Join(", ", items.Select(i => i.Count <= 1
            ? i.Name
            : $"{i.Name} × {i.Count.ToString(CultureInfo.InvariantCulture)}"));

    private static string PhaseLabel(GameTimePhase phase) => phase switch
    {
        GameTimePhase.Start => "Start",
        GameTimePhase.Early or GameTimePhase.Midgame => "Hra",
        GameTimePhase.Lategame or GameTimePhase.EndGame => "Konec",
        _ => phase.ToString()
    };

    private static int PhaseOrder(GameTimePhase phase) => phase switch
    {
        GameTimePhase.Start => 0,
        GameTimePhase.Early or GameTimePhase.Midgame => 1,
        GameTimePhase.Lategame or GameTimePhase.EndGame => 2,
        _ => 3
    };

    private static void DrawWrappedText(
        IImageProcessingContext ctx,
        string text,
        Font font,
        Color color,
        float x,
        float y,
        float width,
        float height,
        int maxLines,
        bool center)
    {
        var lines = WrapText(text, font, width, maxLines);
        var lineHeight = font.Size * 1.2f;
        var totalHeight = lines.Count * lineHeight;
        var startY = center ? y + Math.Max(0, (height - totalHeight) / 2) : y;

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var lineX = x;
            if (center)
            {
                var size = TextMeasurer.MeasureSize(line, new TextOptions(font));
                lineX = x + Math.Max(0, (width - size.Width) / 2);
            }

            ctx.DrawText(line, font, color, new PointF(lineX, startY + i * lineHeight));
        }
    }

    private static List<string> WrapText(string text, Font font, float maxWidth, int maxLines)
    {
        var tokens = Tokenize(text).ToList();
        var lines = new List<string>();
        var current = string.Empty;

        foreach (var token in tokens)
        {
            var candidate = string.IsNullOrWhiteSpace(current) ? token : $"{current} {token}";
            if (TextFits(candidate, font, maxWidth))
            {
                current = candidate;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(current))
                lines.Add(current);

            if (lines.Count == maxLines)
            {
                lines[^1] = FitText(lines[^1], font, maxWidth);
                return lines;
            }

            current = TextFits(token, font, maxWidth) ? token : FitText(token, font, maxWidth);
        }

        if (!string.IsNullOrWhiteSpace(current) && lines.Count < maxLines)
            lines.Add(current);

        if (lines.Count > maxLines)
            lines = lines.Take(maxLines).ToList();

        return lines;
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        foreach (var raw in text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = raw.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length <= 1)
            {
                yield return raw;
                continue;
            }

            for (var i = 0; i < parts.Length; i++)
                yield return i == parts.Length - 1 ? parts[i] : $"{parts[i]}-";
        }
    }

    private static bool TextFits(string text, Font font, float maxWidth) =>
        TextMeasurer.MeasureSize(text, new TextOptions(font)).Width <= maxWidth;

    private static string FitText(string text, Font font, float maxWidth)
    {
        if (TextFits(text, font, maxWidth))
            return text;

        const string ellipsis = "...";
        var lo = 0;
        var hi = text.Length;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            var candidate = text[..mid].TrimEnd() + ellipsis;
            if (TextFits(candidate, font, maxWidth))
                lo = mid;
            else
                hi = mid - 1;
        }

        return lo <= 0 ? ellipsis : text[..lo].TrimEnd() + ellipsis;
    }

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
        ctx.DrawText(text, font, color, new PointF(x + (width - size.Width) / 2, y));
    }

    private static string Pt(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private sealed record LibrarianTreasureItem(string Name, int Count);

    private sealed record LibrarianTreasureRow(string Contents, string Place, string Phase, int PhaseOrder);

    private sealed record LibrarianTreasureLayout(
        int Columns,
        int RowsPerColumn,
        int PageCount,
        int StripHeightPx,
        int BorderThicknessPx,
        int CellsPerStrip);

    private sealed record LibrarianTreasureFonts(
        Font Title,
        Font Subtitle,
        Font Header,
        Font Body,
        Font Phase,
        Font Small);
}

internal sealed record LibrarianTreasureLayoutForTesting(
    int Columns,
    int RowsPerColumn,
    int PageCount,
    int StripHeightPx,
    int BorderThicknessPx,
    int CellsPerStrip);
