using System.Diagnostics;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Services.Pdf;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Domain.ValueObjects;
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
    private const int MarginPx = 60;
    private const int HeaderHeightPx = 210;
    private const int TableHeaderHeightPx = 72;
    private const int FooterHeightPx = 70;
    private const int MinFontPx = 5;
    private const int MaxFontPx = 30;
    private const int ColumnCount = 8;
    private const float BorderPx = 1.6f;

    private static readonly Color Paper = Color.White;
    private static readonly Color Ink = Color.Black;
    private static readonly Color MutedInk = Color.ParseHex("#333333");
    private static readonly Color HeaderFill = Color.ParseHex("#D9D9D9");
    private static readonly string[] Headers =
    [
        "Název",
        "Typ",
        "Efekt",
        "Cena",
        "Válečník",
        "Střelec",
        "Kouzelník",
        "Zloděj"
    ];
    private static readonly float[] ColumnWeights =
    [
        0.25f,
        0.12f,
        0.17f,
        0.07f,
        0.0975f,
        0.0975f,
        0.0975f,
        0.0975f
    ];
    private static readonly StringComparer CzechComparer =
        StringComparer.Create(CultureInfo.GetCultureInfo("cs-CZ"), ignoreCase: false);

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
        var rows = await db.GameItems
            .AsNoTracking()
            .Where(gi => gi.GameId == gameId
                && gi.IsSold
                && gi.Price.HasValue
                && gi.Price.Value > 0)
            .Select(gi => new
            {
                gi.Item.Name,
                gi.Item.ItemType,
                gi.Item.Effect,
                Price = gi.Price!.Value,
                ReqWarrior = gi.Item.ClassRequirements.Warrior,
                ReqArcher = gi.Item.ClassRequirements.Archer,
                ReqMage = gi.Item.ClassRequirements.Mage,
                ReqThief = gi.Item.ClassRequirements.Thief
            })
            .ToListAsync(ct);

        var items = rows
            .Select(r => new CenikItem(
                r.Name,
                r.ItemType,
                r.Effect,
                r.Price,
                new ClassRequirements(r.ReqWarrior, r.ReqArcher, r.ReqMage, r.ReqThief)))
            .OrderBy(i => TypeOrder(i.Type))
            .ThenBy(i => i.TypeDisplay, CzechComparer)
            .ThenBy(i => i.Name, CzechComparer)
            .ToList();

        logger.LogInformation(
            "[export-server] cenik.query items={ItemCount} elapsedMs={ElapsedMs}",
            items.Count,
            queryStopwatch.ElapsedMilliseconds);

        if (items.Count == 0)
            throw new CenikExportProblemException("Vybraná hra nemá žádné prodejné předměty.");

        var renderStopwatch = Stopwatch.StartNew();
        var layout = CalculateLayout(items.Count);
        var pdfImage = await RenderPageAsync(game.Name, items, layout, ct);
        logger.LogInformation(
            "[export-server] cenik.row-count items={ItemCount} pages={Pages} elapsedMs={ElapsedMs}",
            items.Count,
            layout.PageCount,
            renderStopwatch.ElapsedMilliseconds);
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

    internal static CenikLayoutForTesting CalculateLayoutForTesting(int itemCount)
    {
        var layout = CalculateLayout(itemCount);
        return new CenikLayoutForTesting(layout.Columns, layout.FontSizePx, layout.RowHeightPx, layout.PageCount);
    }

    internal static string FormatPriceForTesting(int price) => FormatPrice(price);

    internal static string TypeRowColorHexForTesting(ItemType type) => TypeRowColorHex(type);

    internal static string? ClassRequirementColorHexForTesting(int requirement) =>
        ClassRequirementColorHex(requirement);

    private static CenikLayout CalculateLayout(int itemCount)
    {
        var tableHeight = A4HeightPx - MarginPx * 2 - HeaderHeightPx - TableHeaderHeightPx - FooterHeightPx;
        var rowHeight = tableHeight / (float)Math.Max(1, itemCount);
        var fontSize = Math.Clamp((int)Math.Floor(rowHeight * 0.44f), MinFontPx, MaxFontPx);
        return new CenikLayout(ColumnCount, fontSize, rowHeight, PageCount: 1);
    }

    private async Task<PdfImage> RenderPageAsync(
        string gameName,
        IReadOnlyList<CenikItem> items,
        CenikLayout layout,
        CancellationToken ct)
    {
        var fonts = LoadFonts(layout.FontSizePx);
        using var image = new Image<Rgba32>(A4WidthPx, A4HeightPx, Paper);
        image.Mutate(ctx =>
        {
            DrawHeader(ctx, gameName, fonts);
            DrawTable(ctx, items, layout, fonts);
            DrawFooter(ctx, fonts);
        });

        await using var stream = new MemoryStream();
        await image.SaveAsJpegAsync(stream, new JpegEncoder { Quality = 95 }, ct);
        return new PdfImage(A4WidthPx, A4HeightPx, stream.ToArray());
    }

    private void DrawHeader(IImageProcessingContext ctx, string gameName, CenikFonts fonts)
    {
        DrawCenteredText(ctx, "Ceník", fonts.Title, Ink, MarginPx, 42, A4WidthPx - MarginPx * 2);
        DrawCenteredText(ctx, gameName, fonts.Subtitle, MutedInk, MarginPx, 114, A4WidthPx - MarginPx * 2);
        ctx.Draw(Ink, 3, new RectangularPolygon(MarginPx, 182, A4WidthPx - MarginPx * 2, 1));
    }

    private static void DrawTable(
        IImageProcessingContext ctx,
        IReadOnlyList<CenikItem> items,
        CenikLayout layout,
        CenikFonts fonts)
    {
        var tableX = MarginPx;
        var tableY = MarginPx + HeaderHeightPx;
        var tableWidth = A4WidthPx - MarginPx * 2;
        var columnWidths = CalculateColumnWidths(tableWidth);
        DrawHeaderRow(ctx, tableX, tableY, columnWidths, fonts);

        var y = (float)(tableY + TableHeaderHeightPx);
        foreach (var item in items)
        {
            DrawItemRow(ctx, item, tableX, y, columnWidths, layout.RowHeightPx, fonts);
            y += layout.RowHeightPx;
        }
    }

    private static void DrawHeaderRow(
        IImageProcessingContext ctx,
        float x,
        float y,
        IReadOnlyList<float> columnWidths,
        CenikFonts fonts)
    {
        var cellX = x;
        for (var i = 0; i < Headers.Length; i++)
        {
            DrawCell(
                ctx,
                Headers[i],
                fonts.Header,
                HeaderFill,
                cellX,
                y,
                columnWidths[i],
                TableHeaderHeightPx,
                Align.Center);
            cellX += columnWidths[i];
        }
    }

    private static void DrawItemRow(
        IImageProcessingContext ctx,
        CenikItem item,
        float x,
        float y,
        IReadOnlyList<float> columnWidths,
        float rowHeight,
        CenikFonts fonts)
    {
        var rowFill = Color.ParseHex(TypeRowColorHex(item.Type));
        var cells = new[]
        {
            new CenikCell(item.Name, rowFill, Align.Left),
            new CenikCell(item.TypeDisplay, rowFill, Align.Left),
            new CenikCell(item.EffectText, rowFill, Align.Left),
            new CenikCell(FormatPrice(item.Price), rowFill, Align.Center),
            ClassRequirementCell(item.Requirements.Warrior),
            ClassRequirementCell(item.Requirements.Archer),
            ClassRequirementCell(item.Requirements.Mage),
            ClassRequirementCell(item.Requirements.Thief)
        };

        var cellX = x;
        for (var i = 0; i < cells.Length; i++)
        {
            DrawCell(
                ctx,
                cells[i].Text,
                i == 3 || i >= 4 ? fonts.BodyBold : fonts.Body,
                cells[i].Fill,
                cellX,
                y,
                columnWidths[i],
                rowHeight,
                cells[i].Align);
            cellX += columnWidths[i];
        }
    }

    private static CenikCell ClassRequirementCell(int requirement)
    {
        var colorHex = ClassRequirementColorHex(requirement);
        return new CenikCell(
            colorHex is null ? string.Empty : requirement.ToString(CultureInfo.InvariantCulture),
            colorHex is null ? Paper : Color.ParseHex(colorHex),
            Align.Center);
    }

    private static void DrawCell(
        IImageProcessingContext ctx,
        string text,
        Font font,
        Color fill,
        float x,
        float y,
        float width,
        float height,
        Align align)
    {
        var rect = new RectangularPolygon(x, y, width, height);
        ctx.Fill(fill, rect);
        ctx.Draw(Ink, BorderPx, rect);

        if (string.IsNullOrWhiteSpace(text))
            return;

        var fitted = FitText(text, font, width - 14);
        var size = TextMeasurer.MeasureSize(fitted, new TextOptions(font));
        var textX = align switch
        {
            Align.Center => x + Math.Max(0, (width - size.Width) / 2),
            Align.Right => x + width - size.Width - 7,
            _ => x + 7
        };
        var textY = y + Math.Max(1, (height - size.Height) / 2);
        ctx.DrawText(fitted, font, Ink, new PointF(textX, textY));
    }

    private static void DrawFooter(IImageProcessingContext ctx, CenikFonts fonts)
    {
        DrawCenteredText(
            ctx,
            "Všechny ceny v groších",
            fonts.Footer,
            MutedInk,
            MarginPx,
            A4HeightPx - 58,
            A4WidthPx - MarginPx * 2);
    }

    private CenikFonts LoadFonts(float itemFontSize)
    {
        var stopwatch = Stopwatch.StartNew();
        var fontRoot = System.IO.Path.Combine(environment.ContentRootPath, "Fonts", "Inter");
        var regularPath = System.IO.Path.Combine(fontRoot, "Inter-Regular.ttf");
        var boldPath = System.IO.Path.Combine(fontRoot, "Inter-Bold.ttf");
        try
        {
            var fonts = new FontCollection();
            var regular = fonts.Add(regularPath);
            var bold = fonts.Add(boldPath);
            logger.LogInformation(
                "[export-server] cenik.font-loaded path={Path} family={Family} elapsedMs={ElapsedMs}",
                regularPath,
                regular.Name,
                stopwatch.ElapsedMilliseconds);

            return new CenikFonts(
                Title: bold.CreateFont(56),
                Subtitle: regular.CreateFont(30),
                Header: bold.CreateFont(Math.Max(6, itemFontSize * 0.82f)),
                Body: regular.CreateFont(itemFontSize),
                BodyBold: bold.CreateFont(itemFontSize),
                Footer: regular.CreateFont(24));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[export-server] cenik.font-load-failed path={Path}", fontRoot);
            throw new CenikExportProblemException("Čitelný font pro export ceníku není dostupný.");
        }
    }

    private static IReadOnlyList<float> CalculateColumnWidths(float tableWidth)
    {
        var widths = new float[ColumnWeights.Length];
        var used = 0f;
        for (var i = 0; i < ColumnWeights.Length - 1; i++)
        {
            widths[i] = MathF.Round(tableWidth * ColumnWeights[i]);
            used += widths[i];
        }

        widths[^1] = tableWidth - used;
        return widths;
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

    private static int TypeOrder(ItemType type) => type switch
    {
        ItemType.Weapon => 0,
        ItemType.Shield => 1,
        ItemType.Armor => 2,
        ItemType.Helmet => 3,
        ItemType.Firearm => 4,
        ItemType.Resource => 5,
        _ => 50
    };

    private static string TypeRowColorHex(ItemType type) => type switch
    {
        ItemType.Weapon => "#F5DEB3",
        ItemType.Shield => "#E1F4D8",
        ItemType.Armor => "#D8E4F0",
        ItemType.Helmet => "#FFC080",
        ItemType.Firearm => "#E0F0F8",
        _ => "#FFFFFF"
    };

    private static string? ClassRequirementColorHex(int requirement) => requirement switch
    {
        1 => "#FFFF66",
        2 => "#A0F0A0",
        3 => "#FF6666",
        4 => "#A0C0F0",
        5 => "#C080F0",
        _ => null
    };

    private static string FormatPrice(int price) => price.ToString(CultureInfo.InvariantCulture);

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

    private enum Align
    {
        Left,
        Center,
        Right
    }

    private sealed record CenikCell(string Text, Color Fill, Align Align);

    private sealed record CenikItem(
        string Name,
        ItemType Type,
        string? Effect,
        int Price,
        ClassRequirements Requirements)
    {
        public string TypeDisplay => Type == ItemType.Resource ? "Magická energie" : Type.GetDisplayName();
        public string EffectText => string.IsNullOrWhiteSpace(Effect) ? string.Empty : Effect.Trim();
    }

    private sealed record CenikLayout(int Columns, float FontSizePx, float RowHeightPx, int PageCount);

    private sealed record CenikFonts(
        Font Title,
        Font Subtitle,
        Font Header,
        Font Body,
        Font BodyBold,
        Font Footer);
}

internal sealed record CenikLayoutForTesting(int Columns, float FontSizePx, float RowHeightPx, int PageCount);
