using System.Diagnostics;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Services.Pdf;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Extensions;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace OvcinaHra.Api.Services;

public interface ICenikExportService
{
    Task<CenikExportFile> RenderCenikAsync(int gameId, CancellationToken ct = default);
}

public sealed record CenikExportFile(byte[] Bytes, string FileName);

public sealed class CenikExportProblemException(string detail)
    : Exception(detail)
{
    public string Title { get; } = "Export ceníku se nepodařil";
    public string Detail { get; } = detail;
}

public sealed class CenikExportService(
    WorldDbContext db,
    IWebHostEnvironment environment,
    ILogger<CenikExportService> logger) : ICenikExportService
{
    private const double A4WidthPt = 595.276;
    private const double A4HeightPt = 841.89;
    private const int A4WidthPx = 2480;
    private const int A4HeightPx = 3508;
    private const int MarginPx = 96;
    private const int HeaderHeightPx = 255;
    private const int FooterHeightPx = 58;
    private const int ColumnGapPx = 44;
    private const int MinFontPx = 5;
    private const int MaxFontPx = 38;
    private const int MaxColumns = 4;

    private static readonly Color Paper = Color.ParseHex("#fff8e8");
    private static readonly Color Ink = Color.ParseHex("#24170f");
    private static readonly Color MutedInk = Color.ParseHex("#6d5b44");
    private static readonly Color Border = Color.ParseHex("#5a3b1d");
    private static readonly Color Green = Color.ParseHex("#2d5016");
    private static readonly Color Gold = Color.ParseHex("#c79b34");

    public async Task<CenikExportFile> RenderCenikAsync(int gameId, CancellationToken ct = default)
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
        var items = await db.GameItems
            .AsNoTracking()
            .Where(gi => gi.GameId == gameId
                && gi.IsSold
                && gi.Price.HasValue
                && gi.Price.Value > 0)
            .Select(gi => new CenikItem(
                gi.Item.Name,
                gi.Item.ItemType,
                gi.Price!.Value))
            .ToListAsync(ct);
        items = items
            .OrderBy(i => i.TypeDisplay, StringComparer.CurrentCulture)
            .ThenBy(i => i.Name, StringComparer.CurrentCulture)
            .ToList();

        logger.LogInformation(
            "[export-server] cenik.query items={ItemCount} elapsedMs={ElapsedMs}",
            items.Count,
            queryStopwatch.ElapsedMilliseconds);

        if (items.Count == 0)
            throw new CenikExportProblemException("Vybraná hra nemá žádné prodejné předměty.");

        var renderStopwatch = Stopwatch.StartNew();
        var entries = BuildEntries(items);
        var layout = CalculateLayout(entries.Count);
        var pdfImage = await RenderPageAsync(game.Name, entries, layout, ct);
        var pdf = SimpleImagePdfWriter.Build([
            new PdfImagePage(
                A4WidthPt,
                A4HeightPt,
                $"q {Pt(A4WidthPt)} 0 0 {Pt(A4HeightPt)} 0 0 cm /CenikPage Do Q\n",
                new Dictionary<string, PdfImage> { ["CenikPage"] = pdfImage })
        ]);

        logger.LogInformation(
            "[export-server] cenik.render fontSize={FontSize} columns={Columns} elapsedMs={ElapsedMs}",
            layout.FontSizePx,
            layout.Columns,
            renderStopwatch.ElapsedMilliseconds);

        var fileName = ExportFilenameBuilder.BuildExportFilename("Cenik", game.Name, includeDate: true);
        logger.LogInformation(
            "[export-server] cenik.exit status=200 elapsedMs={ElapsedMs}",
            totalStopwatch.ElapsedMilliseconds);
        return new CenikExportFile(pdf, fileName);
    }

    internal static CenikLayoutForTesting CalculateLayoutForTesting(int entryCount)
    {
        var layout = CalculateLayout(entryCount);
        return new CenikLayoutForTesting(layout.Columns, layout.FontSizePx, layout.RowsPerColumn);
    }

    private static CenikLayout CalculateLayout(int entryCount)
    {
        var contentHeight = A4HeightPx - MarginPx - HeaderHeightPx - FooterHeightPx;
        CenikLayout? best = null;
        for (var columns = 1; columns <= MaxColumns; columns++)
        {
            var rowsPerColumn = Math.Max(1, (int)Math.Ceiling(entryCount / (double)columns));
            var fontByHeight = (int)Math.Floor(contentHeight / (rowsPerColumn * 1.35));
            var fontSize = Math.Clamp(fontByHeight, MinFontPx, MaxFontPx);
            var candidate = new CenikLayout(columns, fontSize, rowsPerColumn);
            if (best is null
                || candidate.FontSizePx > best.FontSizePx
                || (Math.Abs(candidate.FontSizePx - best.FontSizePx) < 0.01f && candidate.Columns < best.Columns))
            {
                best = candidate;
            }
        }

        return best ?? new CenikLayout(MaxColumns, MinFontPx, Math.Max(1, entryCount));
    }

    private async Task<PdfImage> RenderPageAsync(
        string gameName,
        IReadOnlyList<CenikEntry> entries,
        CenikLayout layout,
        CancellationToken ct)
    {
        var fonts = LoadFonts(layout.FontSizePx);
        using var image = new Image<Rgba32>(A4WidthPx, A4HeightPx, Paper);
        image.Mutate(ctx =>
        {
            DrawPageFrame(ctx);
            DrawHeader(ctx, gameName, fonts);
            DrawEntries(ctx, entries, layout, fonts);
            DrawFooter(ctx, fonts);
        });

        await using var stream = new MemoryStream();
        await image.SaveAsJpegAsync(stream, new JpegEncoder { Quality = 94 }, ct);
        return new PdfImage(A4WidthPx, A4HeightPx, stream.ToArray());
    }

    private void DrawPageFrame(IImageProcessingContext ctx)
    {
        ctx.Draw(Border, 5, new RectangularPolygon(36, 36, A4WidthPx - 72, A4HeightPx - 72));
        ctx.Draw(Gold, 3, new RectangularPolygon(54, 54, A4WidthPx - 108, A4HeightPx - 108));
    }

    private void DrawHeader(IImageProcessingContext ctx, string gameName, CenikFonts fonts)
    {
        DrawCenteredText(ctx, "Ceník", fonts.Title, Green, MarginPx, 74, A4WidthPx - MarginPx * 2);
        DrawCenteredText(ctx, gameName, fonts.Subtitle, Ink, MarginPx, 168, A4WidthPx - MarginPx * 2);
        ctx.Draw(Border, 3, new RectangularPolygon(MarginPx, 232, A4WidthPx - MarginPx * 2, 1));
    }

    private static void DrawEntries(
        IImageProcessingContext ctx,
        IReadOnlyList<CenikEntry> entries,
        CenikLayout layout,
        CenikFonts fonts)
    {
        var contentTop = MarginPx + HeaderHeightPx;
        var contentHeight = A4HeightPx - contentTop - FooterHeightPx;
        var columnWidth = (A4WidthPx - MarginPx * 2 - (layout.Columns - 1) * ColumnGapPx) / layout.Columns;
        var rowHeight = contentHeight / (float)layout.RowsPerColumn;

        for (var i = 0; i < entries.Count; i++)
        {
            var column = i / layout.RowsPerColumn;
            var row = i % layout.RowsPerColumn;
            var x = MarginPx + column * (columnWidth + ColumnGapPx);
            var y = contentTop + row * rowHeight;

            if (entries[i] is CenikHeading heading)
            {
                var headingBox = new RectangularPolygon(x, y + 3, columnWidth, rowHeight - 6);
                ctx.Fill(Green, headingBox);
                ctx.Draw(Border, 1.5f, headingBox);
                DrawText(ctx, FitText(heading.Text, fonts.Category, columnWidth - 18), fonts.Category, Color.White, x + 9, y + 6);
                continue;
            }

            var item = (CenikItemEntry)entries[i];
            var price = FormatPrice(item.Price);
            var priceWidth = TextMeasurer.MeasureSize(price, new TextOptions(fonts.Price)).Width;
            var nameWidth = columnWidth - priceWidth - 26;
            var name = FitText(item.Name, fonts.ItemName, nameWidth);
            DrawText(ctx, name, fonts.ItemName, Ink, x + 4, y + 4);
            DrawText(ctx, price, fonts.Price, Ink, x + columnWidth - priceWidth - 4, y + 4);
            ctx.Draw(Color.ParseHex("#dbcaa4"), 1, new RectangularPolygon(x, y + rowHeight - 3, columnWidth, 1));
        }
    }

    private static void DrawFooter(IImageProcessingContext ctx, CenikFonts fonts)
    {
        DrawCenteredText(
            ctx,
            "Všechny ceny jsou uvedené v groších.",
            fonts.Footer,
            MutedInk,
            MarginPx,
            A4HeightPx - 92,
            A4WidthPx - MarginPx * 2);
    }

    private CenikFonts LoadFonts(float itemFontSize)
    {
        var fonts = new FontCollection();
        var fontRoot = System.IO.Path.Combine(environment.ContentRootPath, "Fonts", "Kalam");
        var regular = fonts.Add(System.IO.Path.Combine(fontRoot, "Kalam-Regular.ttf"));
        var bold = fonts.Add(System.IO.Path.Combine(fontRoot, "Kalam-Bold.ttf"));

        return new CenikFonts(
            Title: bold.CreateFont(92),
            Subtitle: regular.CreateFont(38),
            Category: bold.CreateFont(Math.Max(12, itemFontSize * 0.9f)),
            ItemName: bold.CreateFont(itemFontSize),
            Price: bold.CreateFont(itemFontSize),
            Footer: regular.CreateFont(26));
    }

    private static List<CenikEntry> BuildEntries(IReadOnlyList<CenikItem> items)
    {
        var entries = new List<CenikEntry>();
        foreach (var group in items.GroupBy(i => i.TypeDisplay))
        {
            entries.Add(new CenikHeading(group.Key));
            entries.AddRange(group.Select(item => new CenikItemEntry(item.Name, item.Price)));
        }

        return entries;
    }

    private static string FitText(string text, Font font, float maxWidth)
    {
        if (TextMeasurer.MeasureSize(text, new TextOptions(font)).Width <= maxWidth)
            return text;

        const string ellipsis = "...";
        var lo = 0;
        var hi = text.Length;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            var candidate = text[..mid].TrimEnd() + ellipsis;
            if (TextMeasurer.MeasureSize(candidate, new TextOptions(font)).Width <= maxWidth)
                lo = mid;
            else
                hi = mid - 1;
        }

        return lo <= 0 ? ellipsis : text[..lo].TrimEnd() + ellipsis;
    }

    private static string FormatPrice(int price) => price switch
    {
        1 => "1 groš",
        >= 2 and <= 4 => $"{price.ToString(CultureInfo.InvariantCulture)} groše",
        _ => $"{price.ToString(CultureInfo.InvariantCulture)} grošů",
    };

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

    private static string Pt(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private sealed record CenikItem(string Name, ItemType Type, int Price)
    {
        public string TypeDisplay => Type.GetDisplayName();
    }

    private abstract record CenikEntry;

    private sealed record CenikHeading(string Text) : CenikEntry;

    private sealed record CenikItemEntry(string Name, int Price) : CenikEntry;

    private sealed record CenikLayout(int Columns, float FontSizePx, int RowsPerColumn);

    private sealed record CenikFonts(
        Font Title,
        Font Subtitle,
        Font Category,
        Font ItemName,
        Font Price,
        Font Footer);
}

internal sealed record CenikLayoutForTesting(int Columns, float FontSizePx, int RowsPerColumn);
