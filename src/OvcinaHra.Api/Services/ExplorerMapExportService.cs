using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Services.Pdf;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace OvcinaHra.Api.Services;

public interface IExplorerMapExportService
{
    Task<ExplorerMapExportFile> RenderMapAsync(
        int gameId,
        MapExportBasemapStyle style,
        MapExportKind kind,
        MapExportPageFormat pageFormat,
        CancellationToken ct = default);
}

public sealed record ExplorerMapExportFile(byte[] Bytes, string FileName);

internal sealed record MapExportMapArea(double X, double Y, double Width, double Height);

public sealed class MapExportProblemException : Exception
{
    public MapExportProblemException(string detail)
        : this("Export mapy se nepodařil", detail)
    {
    }

    public MapExportProblemException(string title, string detail)
        : base(detail)
    {
        Title = title;
        Detail = detail;
    }

    public string Title { get; }

    public string Detail { get; }
}

public sealed class ExplorerMapExportService(
    WorldDbContext db,
    IMapDataService mapData,
    IMapTileClient tileClient,
    IWebHostEnvironment environment,
    ILogger<ExplorerMapExportService> logger) : IExplorerMapExportService
{
    private const double A4Width = 595.276;
    private const double A4Height = 841.89;
    private const double A3Width = 841.89;
    private const double A3Height = 1190.551;
    private const double PageMargin = 12;
    private const double PixelScale = 2.5;
    private const double LabelRasterScale = 3;
    private const int LabelMaxCharsPerLine = 14;
    private const int LabelMaxLines = 3;
    private const float LabelCandidateGap = 5f * (float)LabelRasterScale;
    private const float LabelCollisionPadding = 1.5f * (float)LabelRasterScale;
    private const float PinRadius = 8f * (float)LabelRasterScale;
    private const float PinTail = 6f * (float)LabelRasterScale;
    private const float PinCollisionPadding = 1f * (float)LabelRasterScale;
    private const int TileFetchConcurrency = 6;

    private static readonly JsonSerializerOptions OverlayJsonOptions = new() { WriteIndented = false };

    public async Task<ExplorerMapExportFile> RenderMapAsync(
        int gameId,
        MapExportBasemapStyle style,
        MapExportKind kind,
        MapExportPageFormat pageFormat,
        CancellationToken ct = default)
    {
        logger.LogInformation(
            "[map-export-pr3] map export entry gameId={GameId} kind={Kind} pageFormat={PageFormat} style={Style}",
            gameId,
            kind,
            pageFormat,
            style);

        var game = await db.Games
            .AsNoTracking()
            .Where(g => g.Id == gameId)
            .Select(g => new
            {
                g.Id,
                g.Name,
                g.Edition,
                g.BoundingBoxSwLat,
                g.BoundingBoxSwLng,
                g.BoundingBoxNeLat,
                g.BoundingBoxNeLng
            })
            .FirstOrDefaultAsync(ct);
        if (game is null)
            throw new KeyNotFoundException();

        if (game.BoundingBoxSwLat is null
            || game.BoundingBoxSwLng is null
            || game.BoundingBoxNeLat is null
            || game.BoundingBoxNeLng is null)
        {
            throw new MapExportProblemException("Hra nemá nastavené hranice mapy.");
        }

        var bounds = new MapBounds(
            South: (double)game.BoundingBoxSwLat.Value,
            West: (double)game.BoundingBoxSwLng.Value,
            North: (double)game.BoundingBoxNeLat.Value,
            East: (double)game.BoundingBoxNeLng.Value);
        if (!bounds.IsValid)
            throw new MapExportProblemException("Hranice mapy nejsou platné.");

        var data = await mapData.GetMapDataAsync(gameId, ct);
        var locations = data.Locations
            .Where(l => bounds.Contains(l.Lat, l.Lon))
            .OrderBy(l => l.EffectiveKind == LocationKind.Hobbit ? 1 : 0)
            .ThenBy(l => l.Id)
            .ToList();

        var page = PageGeometry.For(pageFormat);
        var layout = ExportLayout.For(page.Width, page.Height, bounds.AspectRatio);
        var mapImage = await RenderBasemapAsync(style, bounds, layout, ct);
        var overlays = await LoadOverlaysAsync(gameId, kind, ct);
        var labelOverlay = RenderPinLabelOverlay(page, layout, bounds, locations, kind, ct);

        var canvas = new PdfCanvas(page.Height);
        canvas.FillRectangle(0, 0, page.Width, page.Height, "#ffffff");
        canvas.FillRectangle(layout.MapX, layout.MapY, layout.MapWidth, layout.MapHeight, "#f8f5ed");
        canvas.BeginClip(layout.MapX, layout.MapY, layout.MapWidth, layout.MapHeight);
        if (mapImage is not null)
            canvas.DrawImage("Im1", layout.MapX, layout.MapY, layout.MapWidth, layout.MapHeight);
        foreach (var overlay in overlays)
            RenderOverlay(canvas, layout, bounds, overlay);
        RenderPins(canvas, layout, bounds, locations, kind);
        if (labelOverlay is not null)
            canvas.DrawImage("Labels", 0, 0, page.Width, page.Height);
        canvas.EndClip();
        canvas.StrokeRectangle(layout.MapX, layout.MapY, layout.MapWidth, layout.MapHeight, 0.8, "#6f6658");

        var images = new Dictionary<string, PdfImage>();
        if (mapImage is not null)
            images["Im1"] = mapImage;
        if (labelOverlay is not null)
            images["Labels"] = labelOverlay;

        var pdf = SimpleImagePdfWriter.Build([
            new PdfImagePage(page.Width, page.Height, canvas.Content.ToString(), images)
        ]);

        var fileName = BuildFileName(game.Name, kind);
        logger.LogInformation(
            "[map-export-pr3] map export exit gameId={GameId} kind={Kind} pageFormat={PageFormat} fileName={FileName} bytes={ByteCount} overlays={OverlayCount}",
            gameId,
            kind,
            pageFormat,
            fileName,
            pdf.Length,
            overlays.Count);
        return new ExplorerMapExportFile(pdf, fileName);
    }

    private async Task<IReadOnlyList<MapOverlayDto>> LoadOverlaysAsync(
        int gameId,
        MapExportKind kind,
        CancellationToken ct)
    {
        var audiences = kind switch
        {
            MapExportKind.Organizer => new[] { MapOverlayAudience.Player, MapOverlayAudience.Organizer },
            _ => [MapOverlayAudience.Player]
        };

        var rows = await db.GameMapOverlays
            .AsNoTracking()
            .Where(o => o.GameId == gameId && audiences.Contains(o.Audience))
            .OrderBy(o => o.Audience == MapOverlayAudience.Player ? 0 : 1)
            .Select(o => o.OverlayJson)
            .ToListAsync(ct);

        return rows
            .Select(DeserializeOverlay)
            .Where(o => o is not null)
            .Select(o => o!)
            .ToList();
    }

    private async Task<PdfImage?> RenderBasemapAsync(
        MapExportBasemapStyle style,
        MapBounds bounds,
        ExportLayout layout,
        CancellationToken ct)
    {
        if (style == MapExportBasemapStyle.Blank)
            return null;

        var width = Math.Max(600, (int)Math.Round(layout.MapWidth * PixelScale));
        var height = Math.Max(600, (int)Math.Round(layout.MapHeight * PixelScale));
        var zoom = ChooseZoom(bounds, width, height);

        var westPx = Mercator.WorldPixelX(bounds.West, zoom);
        var eastPx = Mercator.WorldPixelX(bounds.East, zoom);
        var northPx = Mercator.WorldPixelY(bounds.North, zoom);
        var southPx = Mercator.WorldPixelY(bounds.South, zoom);
        var sourceWidth = Math.Max(1, (int)Math.Ceiling(eastPx - westPx));
        var sourceHeight = Math.Max(1, (int)Math.Ceiling(southPx - northPx));

        using var stitched = new Image<Rgba32>(sourceWidth, sourceHeight, Color.White);
        var minTileX = (int)Math.Floor(westPx / 256);
        var maxTileX = (int)Math.Floor((eastPx - 1) / 256);
        var minTileY = (int)Math.Floor(northPx / 256);
        var maxTileY = (int)Math.Floor((southPx - 1) / 256);

        var tileRequests = new List<(int TileX, int TileY, int X, int Y)>();
        for (var tx = minTileX; tx <= maxTileX; tx++)
        {
            for (var ty = minTileY; ty <= maxTileY; ty++)
            {
                var x = (int)Math.Round(tx * 256 - westPx);
                var y = (int)Math.Round(ty * 256 - northPx);
                tileRequests.Add((tx, ty, x, y));
            }
        }

        var tilesToDraw = new (Image<Rgba32>? Tile, int X, int Y)[tileRequests.Count];
        using var fetchGate = new SemaphoreSlim(TileFetchConcurrency);
        try
        {
            var fetchTasks = tileRequests.Select(async (request, index) =>
            {
                await fetchGate.WaitAsync(ct);
                try
                {
                    ct.ThrowIfCancellationRequested();
                    var tile = await tileClient.GetTileAsync(style, zoom, request.TileX, request.TileY, ct);
                    tilesToDraw[index] = (tile, request.X, request.Y);
                }
                finally
                {
                    fetchGate.Release();
                }
            });
            await Task.WhenAll(fetchTasks);

            stitched.Mutate(ctx =>
            {
                foreach (var (tile, x, y) in tilesToDraw)
                {
                    if (tile is not null)
                        ctx.DrawImage(tile, new Point(x, y), 1f);
                }

                ctx.Resize(width, height);
            });
        }
        finally
        {
            foreach (var (tile, _, _) in tilesToDraw)
                tile?.Dispose();
        }

        await using var stream = new MemoryStream();
        await stitched.SaveAsJpegAsync(stream, new JpegEncoder { Quality = 88 }, ct);
        logger.LogInformation(
            "[map-export-pr3] rendered map basemap style={Style} zoom={Zoom} tiles={TileCount}",
            style,
            zoom,
            (maxTileX - minTileX + 1) * (maxTileY - minTileY + 1));
        return new PdfImage(width, height, stream.ToArray());
    }

    private PdfImage? RenderPinLabelOverlay(
        PageGeometry page,
        ExportLayout layout,
        MapBounds bounds,
        IReadOnlyList<MapLocationDto> locations,
        MapExportKind kind,
        CancellationToken ct)
    {
        var labelLocations = locations
            .Where(l => ShouldRenderPinLabel(kind, l.EffectiveKind))
            .ToList();
        var stopwatch = Stopwatch.StartNew();
        logger.LogInformation(
            "[label-place] renderer entry kind={Kind} locations={LocationCount} labels={LabelCount}",
            kind,
            locations.Count,
            labelLocations.Count);
        if (labelLocations.Count == 0)
        {
            logger.LogInformation(
                "[label-place] renderer exit kind={Kind} labels={LabelCount} elapsedMs={ElapsedMs}",
                kind,
                0,
                stopwatch.ElapsedMilliseconds);
            return null;
        }

        var width = (int)Math.Ceiling(page.Width * LabelRasterScale);
        var height = (int)Math.Ceiling(page.Height * LabelRasterScale);
        using var image = new Image<Rgba32>(width, height, Color.Transparent);
        var font = LoadLabelFont(10.5f * (float)LabelRasterScale);
        var ink = Color.ParseHex("#fff8e8");
        var outline = Color.ParseHex("#17120d");
        var pinBounds = locations
            .Select(location => BuildPinBounds(layout.Project(bounds, location.Lat, location.Lon)))
            .ToList();
        var mapBounds = ToRasterRect(layout.MapX, layout.MapY, layout.MapWidth, layout.MapHeight);
        var labelInputs = labelLocations
            .Select(location => new PinLabelInput(
                location.Id,
                location.EffectiveName,
                layout.Project(bounds, location.Lat, location.Lon)))
            .ToList();
        var placements = PlacePinLabels(labelInputs, pinBounds, mapBounds, font.Size);

        image.Mutate(ctx =>
        {
            foreach (var placement in placements)
            {
                logger.LogDebug(
                    "[label-place] placed label='{Label}' id={LocationId} at {Position} attempts={Attempts}",
                    placement.Text,
                    placement.LocationId,
                    PlacementName(placement.Position),
                    placement.Attempts);
                if (placement.UsedFallback)
                {
                    logger.LogInformation(
                        "[label-place] min-overlap fallback used label='{Label}' overlap={OverlapPx}px",
                        placement.Text,
                        (int)MathF.Ceiling(placement.OverlapPx));
                }

                DrawPinLabel(ctx, placement, font, ink, outline);
            }
        });

        var rgb = new byte[width * height * 3];
        var alpha = new byte[width * height];
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var pixel = row[x];
                    var i = y * width + x;
                    rgb[i * 3] = pixel.R;
                    rgb[i * 3 + 1] = pixel.G;
                    rgb[i * 3 + 2] = pixel.B;
                    alpha[i] = pixel.A;
                }
            }
        });

        ct.ThrowIfCancellationRequested();
        logger.LogInformation(
            "[label-place] renderer exit kind={Kind} labels={LabelCount} elapsedMs={ElapsedMs}",
            kind,
            placements.Count,
            stopwatch.ElapsedMilliseconds);
        return SimpleImagePdfWriter.BuildRgbaImage(width, height, rgb, alpha);
    }

    private Font LoadLabelFont(float size)
    {
        var fonts = new FontCollection();
        var fontPath = Path.Combine(environment.ContentRootPath, "Fonts", "Kalam", "Kalam-Regular.ttf");
        var family = fonts.Add(fontPath);
        return family.CreateFont(size);
    }

    private static void DrawPinLabel(
        IImageProcessingContext ctx,
        PinLabelPlacement placement,
        Font font,
        Color ink,
        Color outline)
    {
        for (var i = 0; i < placement.Lines.Count; i++)
        {
            var line = placement.Lines[i];
            var width = ApproxKalamWidth(line, font.Size);
            var x = placement.TextOrigin.X + (placement.TextWidth - width) / 2f;
            var y = placement.TextOrigin.Y + i * placement.LineHeight;
            DrawOutlinedText(ctx, line, font, ink, outline, new PointF(x, y));
        }
    }

    private static void DrawOutlinedText(
        IImageProcessingContext ctx,
        string text,
        Font font,
        Color ink,
        Color outline,
        PointF point)
    {
        var offset = (float)LabelRasterScale;
        foreach (var (dx, dy) in new[] { (-offset, 0f), (offset, 0f), (0f, -offset), (0f, offset) })
            ctx.DrawText(text, font, outline, new PointF(point.X + dx, point.Y + dy));
        ctx.DrawText(text, font, ink, point);
    }

    internal static IReadOnlyList<string> WrapPinLabelForTesting(string text) => WrapPinLabel(text);

    internal static IReadOnlyList<PinLabelPlacementForTesting> PlacePinLabelsForTesting(
        IReadOnlyList<(double X, double Y, string Text)> labels,
        double mapX = 0,
        double mapY = 0,
        double mapWidth = A4Width,
        double mapHeight = A4Height)
    {
        var inputs = labels
            .Select((label, index) => new PinLabelInput(index, label.Text, new PdfPoint(label.X, label.Y)))
            .ToList();
        var pinBounds = inputs.Select(input => BuildPinBounds(input.PinTip)).ToList();
        var placements = PlacePinLabels(
            inputs,
            pinBounds,
            ToRasterRect(mapX, mapY, mapWidth, mapHeight),
            10.5f * (float)LabelRasterScale);
        return placements
            .Select(placement => new PinLabelPlacementForTesting(
                placement.Text,
                PlacementName(placement.Position),
                placement.Bounds.X,
                placement.Bounds.Y,
                placement.Bounds.Width,
                placement.Bounds.Height,
                placement.UsedFallback,
                placement.Attempts))
            .ToList();
    }

    internal static bool ShouldRenderPinLabelForTesting(MapExportKind exportKind, LocationKind locationKind)
        => ShouldRenderPinLabel(exportKind, locationKind);

    internal static MapExportMapArea CalculateMapAreaForTesting(MapExportPageFormat format, double aspectRatio)
    {
        var page = PageGeometry.For(format);
        var layout = ExportLayout.For(page.Width, page.Height, aspectRatio);
        return new MapExportMapArea(layout.MapX, layout.MapY, layout.MapWidth, layout.MapHeight);
    }

    private static IReadOnlyList<string> WrapPinLabel(string text)
    {
        var normalized = text.Trim();
        if (normalized.Length <= LabelMaxCharsPerLine)
            return [normalized];

        var tokens = SplitLabelTokens(normalized);
        var lines = new List<string>();
        var current = string.Empty;

        foreach (var token in tokens)
        {
            if (current.Length == 0)
            {
                current = token;
                continue;
            }

            if (current.Length + 1 + token.Length <= LabelMaxCharsPerLine)
            {
                current += token.StartsWith("-", StringComparison.Ordinal) ? token : " " + token;
                continue;
            }

            lines.Add(current.Trim());
            current = token.TrimStart();
            if (lines.Count == LabelMaxLines - 1)
                break;
        }

        if (lines.Count < LabelMaxLines && !string.IsNullOrWhiteSpace(current))
            lines.Add(current.Trim());

        return lines.Count == 0 ? [normalized] : lines;
    }

    private static List<string> SplitLabelTokens(string text)
    {
        var tokens = new List<string>();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (word.Length <= LabelMaxCharsPerLine || !word.Contains('-'))
            {
                tokens.Add(word);
                continue;
            }

            var parts = word.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (var i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                tokens.Add(i < parts.Length - 1 ? part + "-" : part);
            }
        }

        return tokens;
    }

    private static float ApproxKalamWidth(string text, float fontSize) =>
        text.Sum(ch => char.IsWhiteSpace(ch) ? 0.28f : 0.54f) * fontSize;

    private static IReadOnlyList<PinLabelPlacement> PlacePinLabels(
        IReadOnlyList<PinLabelInput> labels,
        IReadOnlyList<LabelRect> pinBounds,
        LabelRect mapBounds,
        float fontSize)
    {
        var occupied = new List<LabelRect>(pinBounds);
        var placements = new List<PinLabelPlacement>(labels.Count);
        var lineHeight = fontSize * 0.92f;

        foreach (var label in labels)
        {
            var lines = WrapPinLabel(label.Text);
            var textWidth = lines.Max(line => ApproxKalamWidth(line, fontSize));
            var textHeight = lines.Count * lineHeight;
            var candidates = BuildLabelCandidates(label.PinTip, textWidth, textHeight);
            var attempts = 0;
            LabelCandidate? selected = null;
            var bestScore = float.PositiveInfinity;
            var bestOverlapPx = 0f;
            var usedFallback = true;

            foreach (var candidate in candidates)
            {
                attempts++;
                var overlapArea = SumOverlapArea(candidate.Bounds, occupied);
                var outsideArea = candidate.Bounds.OutsideArea(mapBounds);
                var score = overlapArea + outsideArea * 4f;
                var overlapPx = MaxOverlapDepth(candidate.Bounds, occupied);

                if (score <= 0)
                {
                    selected = candidate;
                    bestOverlapPx = 0;
                    usedFallback = false;
                    break;
                }

                if (score < bestScore)
                {
                    selected = candidate;
                    bestScore = score;
                    bestOverlapPx = overlapPx;
                }
            }

            var chosen = selected ?? candidates[0];
            occupied.Add(chosen.Bounds);
            placements.Add(new PinLabelPlacement(
                label.LocationId,
                label.Text,
                lines,
                chosen.Position,
                chosen.TextOrigin,
                chosen.Bounds,
                textWidth,
                lineHeight,
                attempts,
                usedFallback,
                bestOverlapPx));
        }

        return placements;
    }

    private static IReadOnlyList<LabelCandidate> BuildLabelCandidates(
        PdfPoint pinTip,
        float textWidth,
        float textHeight)
    {
        var pin = BuildPinBounds(pinTip);
        var pinCenterX = (float)(pinTip.X * LabelRasterScale);
        var pinCenterY = pin.Y + pin.Height / 2f;

        return
        [
            CreateCandidate(LabelPlacementPosition.AboveLeft, pinCenterX - textWidth - LabelCandidateGap, pin.Y - textHeight - LabelCandidateGap, textWidth, textHeight),
            CreateCandidate(LabelPlacementPosition.Above, pinCenterX - textWidth / 2f, pin.Y - textHeight - LabelCandidateGap, textWidth, textHeight),
            CreateCandidate(LabelPlacementPosition.AboveRight, pinCenterX + LabelCandidateGap, pin.Y - textHeight - LabelCandidateGap, textWidth, textHeight),
            CreateCandidate(LabelPlacementPosition.Right, pin.Right + LabelCandidateGap, pinCenterY - textHeight / 2f, textWidth, textHeight),
            CreateCandidate(LabelPlacementPosition.BelowRight, pinCenterX + LabelCandidateGap, pin.Bottom + LabelCandidateGap, textWidth, textHeight),
            CreateCandidate(LabelPlacementPosition.Below, pinCenterX - textWidth / 2f, pin.Bottom + LabelCandidateGap, textWidth, textHeight),
            CreateCandidate(LabelPlacementPosition.BelowLeft, pinCenterX - textWidth - LabelCandidateGap, pin.Bottom + LabelCandidateGap, textWidth, textHeight),
            CreateCandidate(LabelPlacementPosition.Left, pin.X - textWidth - LabelCandidateGap, pinCenterY - textHeight / 2f, textWidth, textHeight)
        ];
    }

    private static LabelCandidate CreateCandidate(
        LabelPlacementPosition position,
        float x,
        float y,
        float textWidth,
        float textHeight) =>
        new(position, new PointF(x, y), BuildLabelBounds(x, y, textWidth, textHeight));

    private static LabelRect BuildLabelBounds(float x, float y, float width, float height) =>
        new(
            x - LabelCollisionPadding,
            y - LabelCollisionPadding,
            width + LabelCollisionPadding * 2f,
            height + LabelCollisionPadding * 2f);

    private static LabelRect BuildPinBounds(PdfPoint pinTip)
    {
        var tipX = (float)(pinTip.X * LabelRasterScale);
        var tipY = (float)(pinTip.Y * LabelRasterScale);
        return new LabelRect(
            tipX - PinRadius - PinCollisionPadding,
            tipY - PinTail - PinRadius * 2f - PinCollisionPadding,
            PinRadius * 2f + PinCollisionPadding * 2f,
            PinTail + PinRadius * 2f + PinCollisionPadding * 2f);
    }

    private static LabelRect ToRasterRect(double x, double y, double width, double height) =>
        new(
            (float)(x * LabelRasterScale),
            (float)(y * LabelRasterScale),
            (float)(width * LabelRasterScale),
            (float)(height * LabelRasterScale));

    private static float SumOverlapArea(LabelRect rect, IEnumerable<LabelRect> others) =>
        others.Sum(other => rect.OverlapArea(other));

    private static float MaxOverlapDepth(LabelRect rect, IEnumerable<LabelRect> others)
    {
        var max = 0f;
        foreach (var other in others)
        {
            var overlapX = MathF.Min(rect.Right, other.Right) - MathF.Max(rect.X, other.X);
            var overlapY = MathF.Min(rect.Bottom, other.Bottom) - MathF.Max(rect.Y, other.Y);
            if (overlapX > 0 && overlapY > 0)
                max = MathF.Max(max, MathF.Min(overlapX, overlapY));
        }

        return max;
    }

    private static string PlacementName(LabelPlacementPosition position) => position switch
    {
        LabelPlacementPosition.AboveLeft => "above-left",
        LabelPlacementPosition.Above => "above",
        LabelPlacementPosition.AboveRight => "above-right",
        LabelPlacementPosition.Right => "right",
        LabelPlacementPosition.BelowRight => "below-right",
        LabelPlacementPosition.Below => "below",
        LabelPlacementPosition.BelowLeft => "below-left",
        LabelPlacementPosition.Left => "left",
        _ => position.ToString()
    };

    private static int ChooseZoom(MapBounds bounds, int targetWidth, int targetHeight)
    {
        var mercWidth = Math.Abs(Mercator.NormalizedX(bounds.East) - Mercator.NormalizedX(bounds.West));
        var mercHeight = Math.Abs(Mercator.NormalizedY(bounds.South) - Mercator.NormalizedY(bounds.North));
        var zoomForWidth = Math.Log2(targetWidth / (256 * mercWidth));
        var zoomForHeight = Math.Log2(targetHeight / (256 * mercHeight));
        return Math.Clamp((int)Math.Ceiling(Math.Min(zoomForWidth, zoomForHeight)), 0, 17);
    }

    private static MapOverlayDto? DeserializeOverlay(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<MapOverlayDto>(json, OverlayJsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return null;
        }
    }

    private static void RenderOverlay(PdfCanvas canvas, ExportLayout layout, MapBounds bounds, MapOverlayDto? overlay)
    {
        if (overlay?.Shapes is not { Count: > 0 })
            return;

        foreach (var shape in overlay.Shapes)
        {
            switch (shape)
            {
                case TextShape text:
                    var textPoint = layout.Project(bounds, text.Coord.Lat, text.Coord.Lng);
                    canvas.DrawCenteredText(
                        NormalizePdfText(text.Text, 32),
                        textPoint.X,
                        textPoint.Y,
                        Math.Clamp(text.FontSize * 0.75, 6, 18),
                        "#2b251b",
                        "F2");
                    break;
                case FreehandShape freehand:
                    canvas.StrokePolyline(ProjectAll(layout, bounds, freehand.Points), freehand.StrokeWidth, freehand.Color);
                    break;
                case PolylineShape polyline:
                    canvas.StrokePolyline(ProjectAll(layout, bounds, polyline.Points), polyline.StrokeWidth, polyline.Color);
                    break;
                case RectangleShape rectangle:
                    var sw = layout.Project(bounds, rectangle.Sw.Lat, rectangle.Sw.Lng);
                    var ne = layout.Project(bounds, rectangle.Ne.Lat, rectangle.Ne.Lng);
                    canvas.FillAndStrokeRectangleFromCorners(sw, ne, rectangle.FillColor, rectangle.Color, rectangle.StrokeWidth);
                    break;
                case PolygonShape polygon:
                    canvas.FillAndStrokePolygon(ProjectAll(layout, bounds, polygon.Points), polygon.FillColor, polygon.Color, polygon.StrokeWidth);
                    break;
                case CircleShape circle:
                    var center = layout.Project(bounds, circle.Center.Lat, circle.Center.Lng);
                    var radius = layout.ProjectRadius(bounds, circle.Center.Lat, circle.Center.Lng, circle.RadiusMeters);
                    canvas.FillAndStrokeEllipse(center.X, center.Y, radius.X, radius.Y, circle.FillColor, circle.Color, circle.StrokeWidth);
                    break;
                case IconShape icon:
                    var point = layout.Project(bounds, icon.Coord.Lat, icon.Coord.Lng);
                    canvas.FillDiamond(point.X, point.Y, Math.Clamp(5 * icon.Scale, 4, 12), icon.Color);
                    break;
            }
        }
    }

    private static IReadOnlyList<PdfPoint> ProjectAll(ExportLayout layout, MapBounds bounds, IEnumerable<OverlayCoord> points) =>
        points.Select(p => layout.Project(bounds, p.Lat, p.Lng)).ToList();

    private static void RenderPins(
        PdfCanvas canvas,
        ExportLayout layout,
        MapBounds bounds,
        IReadOnlyList<MapLocationDto> locations,
        MapExportKind exportKind)
    {
        foreach (var location in locations)
        {
            var point = layout.Project(bounds, location.Lat, location.Lon);
            var kind = location.EffectiveKind;
            var showName = ShouldRenderPinLabel(exportKind, kind);
            DrawPin(canvas, point.X, point.Y, kind, showName ? null : location.Id.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static bool ShouldRenderPinLabel(MapExportKind exportKind, LocationKind locationKind)
        => exportKind == MapExportKind.Organizer
            || locationKind is LocationKind.Town or LocationKind.Village;

    private static void DrawPin(PdfCanvas canvas, double tipX, double tipY, LocationKind kind, string? innerText)
    {
        var color = KindColor(kind);
        var textColor = kind == LocationKind.Wilderness ? "#111111" : "#ffffff";
        const double radius = 8;
        const double tail = 6;
        var centerY = tipY - tail - radius;

        canvas.FillPolygon(
            [
                new PdfPoint(tipX - 4.8, centerY + 5.5),
                new PdfPoint(tipX + 4.8, centerY + 5.5),
                new PdfPoint(tipX, tipY),
            ],
            color);
        canvas.FillCircle(tipX, centerY, radius, color);
        canvas.StrokeCircle(tipX, centerY, radius, 0.8, "#ffffff");

        if (!string.IsNullOrWhiteSpace(innerText))
        {
            canvas.DrawCenteredText(innerText, tipX, centerY + 2.3, innerText.Length > 2 ? 5.6 : 6.4, textColor, "F2");
        }
        else
        {
            canvas.FillCircle(tipX, centerY, 2.2, textColor);
        }
    }

    private static string KindColor(LocationKind kind) => kind switch
    {
        LocationKind.Town => "#6D6D6D",
        LocationKind.Village => "#FF0000",
        LocationKind.Magical => "#EAF626",
        LocationKind.Hobbit => "#9400D4",
        LocationKind.Wilderness => "#FFFFFF",
        LocationKind.Dungeon => "#000000",
        LocationKind.PointOfInterest => "#FF6600",
        _ => "#2D5016"
    };

    private static string BuildFileName(string gameName, MapExportKind kind)
    {
        var exportType = kind switch
        {
            MapExportKind.Explorer => "PostavyA4",
            MapExportKind.Organizer => "OrganizatorA4",
            MapExportKind.Kingdom => "KralovstviA3",
            _ => "Mapa"
        };

        return ExportFilenameBuilder.BuildExportFilename(exportType, gameName, includeDate: true);
    }

    private static string NormalizePdfText(string value, int maxLength)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var previousWasSpace = false;

        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;

            var current = ch switch
            {
                '–' or '—' => '-',
                '“' or '”' => '"',
                '„' => '"',
                '’' => '\'',
                _ => ch
            };

            if (current is >= 'A' and <= 'Z'
                or >= 'a' and <= 'z'
                or >= '0' and <= '9'
                or ' '
                or '-'
                or '/'
                or '.'
                or ','
                or '\'')
            {
                if (current == ' ')
                {
                    if (previousWasSpace)
                        continue;
                    previousWasSpace = true;
                }
                else
                {
                    previousWasSpace = false;
                }

                builder.Append(current);
            }
        }

        var result = builder.ToString().Trim();
        return result.Length <= maxLength ? result : result[..maxLength].Trim();
    }

    private sealed record MapBounds(double South, double West, double North, double East)
    {
        public bool IsValid => North > South && East > West;
        public double AspectRatio
        {
            get
            {
                var width = Math.Abs(Mercator.NormalizedX(East) - Mercator.NormalizedX(West));
                var height = Math.Abs(Mercator.NormalizedY(South) - Mercator.NormalizedY(North));
                return width / height;
            }
        }

        public bool Contains(double lat, double lon) =>
            lat >= South && lat <= North && lon >= West && lon <= East;
    }

    private sealed record PageGeometry(double Width, double Height)
    {
        public static PageGeometry For(MapExportPageFormat format) => format switch
        {
            MapExportPageFormat.A4Portrait => new PageGeometry(A4Width, A4Height),
            MapExportPageFormat.A3Portrait => new PageGeometry(A3Width, A3Height),
            _ => new PageGeometry(A4Width, A4Height)
        };
    }

    private sealed record ExportLayout(double MapX, double MapY, double MapWidth, double MapHeight)
    {
        public static ExportLayout For(double pageWidth, double pageHeight, double aspectRatio)
        {
            var availableWidth = pageWidth - 2 * PageMargin;
            var availableHeight = pageHeight - 2 * PageMargin;
            var availableAspect = availableWidth / availableHeight;

            double width;
            double height;
            if (aspectRatio >= availableAspect)
            {
                width = availableWidth;
                height = width / aspectRatio;
            }
            else
            {
                height = availableHeight;
                width = height * aspectRatio;
            }

            return new ExportLayout(
                (pageWidth - width) / 2,
                (pageHeight - height) / 2,
                width,
                height);
        }

        public PdfPoint Project(MapBounds bounds, double lat, double lon)
        {
            var x = (Mercator.NormalizedX(lon) - Mercator.NormalizedX(bounds.West))
                / (Mercator.NormalizedX(bounds.East) - Mercator.NormalizedX(bounds.West));
            var y = (Mercator.NormalizedY(lat) - Mercator.NormalizedY(bounds.North))
                / (Mercator.NormalizedY(bounds.South) - Mercator.NormalizedY(bounds.North));
            return new PdfPoint(MapX + x * MapWidth, MapY + y * MapHeight);
        }

        public PdfPoint ProjectRadius(MapBounds bounds, double lat, double lon, double radiusMeters)
        {
            const double metersPerDegree = 111_320d;
            var latOffset = radiusMeters / metersPerDegree;
            var lonOffset = radiusMeters / (metersPerDegree * Math.Max(0.15, Math.Cos(lat * Math.PI / 180)));
            var center = Project(bounds, lat, lon);
            var east = Project(bounds, lat, lon + lonOffset);
            var north = Project(bounds, lat + latOffset, lon);
            return new PdfPoint(Math.Abs(east.X - center.X), Math.Abs(center.Y - north.Y));
        }
    }

    private static class Mercator
    {
        private const double MaxLat = 85.05112878;

        public static double NormalizedX(double lon) => (lon + 180d) / 360d;

        public static double NormalizedY(double lat)
        {
            var clamped = Math.Clamp(lat, -MaxLat, MaxLat);
            var rad = clamped * Math.PI / 180d;
            return (1d - Math.Log(Math.Tan(rad) + 1d / Math.Cos(rad)) / Math.PI) / 2d;
        }

        public static double WorldPixelX(double lon, int zoom) =>
            NormalizedX(lon) * 256d * Math.Pow(2, zoom);

        public static double WorldPixelY(double lat, int zoom) =>
            NormalizedY(lat) * 256d * Math.Pow(2, zoom);
    }

    private sealed record PdfPoint(double X, double Y);

    internal sealed record PinLabelPlacementForTesting(
        string Text,
        string Position,
        float X,
        float Y,
        float Width,
        float Height,
        bool UsedFallback,
        int Attempts);

    private sealed record PinLabelInput(int LocationId, string Text, PdfPoint PinTip);

    private sealed record PinLabelPlacement(
        int LocationId,
        string Text,
        IReadOnlyList<string> Lines,
        LabelPlacementPosition Position,
        PointF TextOrigin,
        LabelRect Bounds,
        float TextWidth,
        float LineHeight,
        int Attempts,
        bool UsedFallback,
        float OverlapPx);

    private sealed record LabelCandidate(
        LabelPlacementPosition Position,
        PointF TextOrigin,
        LabelRect Bounds);

    private enum LabelPlacementPosition
    {
        AboveLeft,
        Above,
        AboveRight,
        Right,
        BelowRight,
        Below,
        BelowLeft,
        Left
    }

    private readonly record struct LabelRect(float X, float Y, float Width, float Height)
    {
        public float Right => X + Width;

        public float Bottom => Y + Height;

        public float Area => Width * Height;

        public float OverlapArea(LabelRect other)
        {
            var overlapX = MathF.Min(Right, other.Right) - MathF.Max(X, other.X);
            var overlapY = MathF.Min(Bottom, other.Bottom) - MathF.Max(Y, other.Y);
            return overlapX <= 0 || overlapY <= 0 ? 0 : overlapX * overlapY;
        }

        public float OutsideArea(LabelRect bounds)
        {
            var insideX1 = MathF.Max(X, bounds.X);
            var insideY1 = MathF.Max(Y, bounds.Y);
            var insideX2 = MathF.Min(Right, bounds.Right);
            var insideY2 = MathF.Min(Bottom, bounds.Bottom);
            var insideWidth = MathF.Max(0, insideX2 - insideX1);
            var insideHeight = MathF.Max(0, insideY2 - insideY1);
            return Area - insideWidth * insideHeight;
        }
    }

    private sealed class PdfCanvas(double pageHeight)
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        public StringBuilder Content { get; } = new();

        public void DrawImage(string name, double x, double y, double width, double height) =>
            Content.AppendLine($"q {Pt(width)} 0 0 {Pt(height)} {Pt(x)} {Pt(ToBottom(y, height))} cm /{name} Do Q");

        public void BeginClip(double x, double y, double width, double height) =>
            Content.AppendLine($"q {Pt(x)} {Pt(ToBottom(y, height))} {Pt(width)} {Pt(height)} re W n");

        public void EndClip() => Content.AppendLine("Q");

        public void FillRectangle(double x, double y, double width, double height, string color) =>
            Content.AppendLine($"{Fill(color)} {Pt(x)} {Pt(ToBottom(y, height))} {Pt(width)} {Pt(height)} re f");

        public void StrokeRectangle(double x, double y, double width, double height, double strokeWidth, string color) =>
            Content.AppendLine($"{Stroke(color)} {Pt(strokeWidth)} w {Pt(x)} {Pt(ToBottom(y, height))} {Pt(width)} {Pt(height)} re S");

        public void FillAndStrokeRectangleFromCorners(PdfPoint a, PdfPoint b, string? fill, string stroke, double strokeWidth)
        {
            var x = Math.Min(a.X, b.X);
            var y = Math.Min(a.Y, b.Y);
            var width = Math.Abs(a.X - b.X);
            var height = Math.Abs(a.Y - b.Y);
            if (!string.IsNullOrWhiteSpace(fill))
                FillRectangle(x, y, width, height, fill);
            StrokeRectangle(x, y, width, height, ScaleStroke(strokeWidth), stroke);
        }

        public void StrokePolyline(IReadOnlyList<PdfPoint> points, double strokeWidth, string color)
        {
            if (points.Count < 2)
                return;

            Content.Append($"{Stroke(color)} {Pt(ScaleStroke(strokeWidth))} w ");
            Content.Append($"{Pt(points[0].X)} {Pt(ToPdfY(points[0].Y))} m ");
            foreach (var point in points.Skip(1))
                Content.Append($"{Pt(point.X)} {Pt(ToPdfY(point.Y))} l ");
            Content.AppendLine("S");
        }

        public void FillAndStrokePolygon(IReadOnlyList<PdfPoint> points, string? fill, string stroke, double strokeWidth)
        {
            if (points.Count < 3)
                return;

            if (!string.IsNullOrWhiteSpace(fill))
                FillPolygon(points, fill);

            Content.Append($"{Stroke(stroke)} {Pt(ScaleStroke(strokeWidth))} w ");
            Content.Append($"{Pt(points[0].X)} {Pt(ToPdfY(points[0].Y))} m ");
            foreach (var point in points.Skip(1))
                Content.Append($"{Pt(point.X)} {Pt(ToPdfY(point.Y))} l ");
            Content.AppendLine("h S");
        }

        public void FillPolygon(IReadOnlyList<PdfPoint> points, string color)
        {
            if (points.Count < 3)
                return;

            Content.Append($"{Fill(color)} {Pt(points[0].X)} {Pt(ToPdfY(points[0].Y))} m ");
            foreach (var point in points.Skip(1))
                Content.Append($"{Pt(point.X)} {Pt(ToPdfY(point.Y))} l ");
            Content.AppendLine("h f");
        }

        public void FillDiamond(double centerX, double centerY, double radius, string color) =>
            FillAndStrokePolygon(
                [
                    new PdfPoint(centerX, centerY - radius),
                    new PdfPoint(centerX + radius, centerY),
                    new PdfPoint(centerX, centerY + radius),
                    new PdfPoint(centerX - radius, centerY),
                ],
                color,
                "#ffffff",
                1);

        public void FillAndStrokeEllipse(double centerX, double centerY, double radiusX, double radiusY, string? fill, string stroke, double strokeWidth)
        {
            if (!string.IsNullOrWhiteSpace(fill))
                DrawEllipse(centerX, centerY, radiusX, radiusY, Fill(fill), "f");
            DrawEllipse(centerX, centerY, radiusX, radiusY, $"{Stroke(stroke)} {Pt(ScaleStroke(strokeWidth))} w", "S");
        }

        public void FillCircle(double centerX, double centerY, double radius, string color) =>
            DrawEllipse(centerX, centerY, radius, radius, Fill(color), "f");

        public void StrokeCircle(double centerX, double centerY, double radius, double strokeWidth, string color) =>
            DrawEllipse(centerX, centerY, radius, radius, $"{Stroke(color)} {Pt(strokeWidth)} w", "S");

        public void DrawCenteredText(string text, double centerX, double baselineY, double fontSize, string color, string fontName)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            var width = ApproxTextWidth(text, fontSize);
            DrawText(text, centerX - width / 2, baselineY, fontSize, color, fontName);
        }

        public void DrawLabel(string text, double x, double baselineY, double fontSize, string color, string background)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            var width = ApproxTextWidth(text, fontSize);
            FillRectangle(x - 2, baselineY - fontSize - 2, width + 4, fontSize + 5, background);
            DrawText(text, x, baselineY, fontSize, color, "F2");
        }

        private void DrawText(string text, double x, double baselineY, double fontSize, string color, string fontName) =>
            Content.AppendLine($"{Fill(color)} BT /{fontName} {Pt(fontSize)} Tf {Pt(x)} {Pt(ToPdfY(baselineY))} Td ({Escape(text)}) Tj ET");

        private void DrawEllipse(double centerX, double centerY, double radiusX, double radiusY, string style, string op)
        {
            const double k = 0.552284749831;
            var rx = Math.Abs(radiusX);
            var ry = Math.Abs(radiusY);
            if (rx <= 0 || ry <= 0)
                return;

            var left = centerX - rx;
            var right = centerX + rx;
            var top = centerY - ry;
            var bottom = centerY + ry;
            var ox = rx * k;
            var oy = ry * k;
            Content.Append($"{style} {Pt(centerX)} {Pt(ToPdfY(top))} m ");
            Content.Append($"{Pt(centerX + ox)} {Pt(ToPdfY(top))} {Pt(right)} {Pt(ToPdfY(centerY - oy))} {Pt(right)} {Pt(ToPdfY(centerY))} c ");
            Content.Append($"{Pt(right)} {Pt(ToPdfY(centerY + oy))} {Pt(centerX + ox)} {Pt(ToPdfY(bottom))} {Pt(centerX)} {Pt(ToPdfY(bottom))} c ");
            Content.Append($"{Pt(centerX - ox)} {Pt(ToPdfY(bottom))} {Pt(left)} {Pt(ToPdfY(centerY + oy))} {Pt(left)} {Pt(ToPdfY(centerY))} c ");
            Content.AppendLine($"{Pt(left)} {Pt(ToPdfY(centerY - oy))} {Pt(centerX - ox)} {Pt(ToPdfY(top))} {Pt(centerX)} {Pt(ToPdfY(top))} c {op}");
        }

        private double ToPdfY(double y) => pageHeight - y;
        private double ToBottom(double y, double height) => pageHeight - y - height;

        private static double ScaleStroke(double strokeWidth) => Math.Clamp(strokeWidth * 0.75, 0.5, 8);
        private static double ApproxTextWidth(string text, double fontSize) => text.Length * fontSize * 0.52;

        private static string Fill(string color) => $"{Rgb(color)} rg";
        private static string Stroke(string color) => $"{Rgb(color)} RG";

        private static string Rgb(string color)
        {
            var normalized = NormalizeColor(color);
            var r = Convert.ToInt32(normalized[1..3], 16) / 255d;
            var g = Convert.ToInt32(normalized[3..5], 16) / 255d;
            var b = Convert.ToInt32(normalized[5..7], 16) / 255d;
            return $"{Pt(r)} {Pt(g)} {Pt(b)}";
        }

        private static string NormalizeColor(string? color)
        {
            if (string.IsNullOrWhiteSpace(color))
                return "#000000";

            var trimmed = color.Trim();
            if (trimmed.Length == 7 && trimmed[0] == '#'
                && trimmed.Skip(1).All(Uri.IsHexDigit))
                return trimmed;

            if (trimmed.Length == 4 && trimmed[0] == '#'
                && trimmed.Skip(1).All(Uri.IsHexDigit))
                return $"#{trimmed[1]}{trimmed[1]}{trimmed[2]}{trimmed[2]}{trimmed[3]}{trimmed[3]}";

            return "#000000";
        }

        private static string Escape(string value) =>
            value.Replace(@"\", @"\\", StringComparison.Ordinal)
                .Replace("(", @"\(", StringComparison.Ordinal)
                .Replace(")", @"\)", StringComparison.Ordinal);

        private static string Pt(double value) => value.ToString("0.###", Invariant);
    }

}

public interface IMapTileClient
{
    Task<Image<Rgba32>> GetTileAsync(
        MapExportBasemapStyle style,
        int zoom,
        int x,
        int y,
        CancellationToken ct = default);
}

public sealed class MapTileClient(
    HttpClient http,
    IConfiguration config,
    IMemoryCache cache,
    ILogger<MapTileClient> logger) : IMapTileClient
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();

    public async Task<Image<Rgba32>> GetTileAsync(
        MapExportBasemapStyle style,
        int zoom,
        int x,
        int y,
        CancellationToken ct = default)
    {
        var (tileX, tileY) = NormalizeTile(zoom, x, y);
        var mapyApiKey = config["MapyCz:ApiKey"];
        if (IsMapyStyle(style) && string.IsNullOrWhiteSpace(mapyApiKey))
        {
            throw new MapExportProblemException(
                "Mapy.cz API klíč není nastaven.",
                "Kontaktujte správce systému.");
        }

        var url = BuildUrl(style, zoom, tileX, tileY, mapyApiKey);
        if (url is null)
            return BlankTile();

        var cacheKey = $"map-tile:{style}:{zoom}:{tileX}:{tileY}";
        if (cache.TryGetValue<byte[]>(cacheKey, out var cached))
            return Image.Load<Rgba32>(cached);

        var gate = Locks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (cache.TryGetValue<byte[]>(cacheKey, out cached))
                return Image.Load<Rgba32>(cached);

            using var response = await http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Map tile request failed style={Style} zoom={Zoom} x={X} y={Y} status={StatusCode}",
                    style,
                    zoom,
                    tileX,
                    tileY,
                    response.StatusCode);
                return BlankTile();
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (string.IsNullOrWhiteSpace(mediaType)
                || !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning(
                    "Map tile request returned non-image content-type={ContentType} style={Style} zoom={Zoom} x={X} y={Y}",
                    mediaType,
                    style,
                    zoom,
                    tileX,
                    tileY);
                return BlankTile();
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            var tile = Image.Load<Rgba32>(bytes);
            cache.Set(cacheKey, bytes, TimeSpan.FromHours(6));
            return tile;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidImageContentException or UnknownImageFormatException)
        {
            cache.Remove(cacheKey);
            logger.LogWarning(
                ex,
                "Map tile request failed style={Style} zoom={Zoom} x={X} y={Y}",
                style,
                zoom,
                tileX,
                tileY);
            return BlankTile();
        }
        finally
        {
            gate.Release();
            if (gate.CurrentCount == 1)
                Locks.TryRemove(cacheKey, out _);
        }
    }

    private static (int X, int Y) NormalizeTile(int zoom, int x, int y)
    {
        var max = 1 << zoom;
        return (((x % max) + max) % max, Math.Clamp(y, 0, max - 1));
    }

    private static bool IsMapyStyle(MapExportBasemapStyle style) =>
        style is MapExportBasemapStyle.Tourist or MapExportBasemapStyle.Aerial or MapExportBasemapStyle.Basic;

    private static string? BuildUrl(MapExportBasemapStyle style, int zoom, int x, int y, string? mapyApiKey)
    {
        return style switch
        {
            MapExportBasemapStyle.Tourist => BuildMapyUrl("outdoor", zoom, x, y, mapyApiKey!),
            MapExportBasemapStyle.Aerial => BuildMapyUrl("aerial", zoom, x, y, mapyApiKey!),
            MapExportBasemapStyle.Basic => BuildMapyUrl("basic", zoom, x, y, mapyApiKey!),
            MapExportBasemapStyle.Osm => $"https://tile.openstreetmap.org/{zoom}/{x}/{y}.png",
            MapExportBasemapStyle.Blank => null,
            _ => null
        };
    }

    private static string BuildMapyUrl(string style, int zoom, int x, int y, string apiKey) =>
        $"https://api.mapy.cz/v1/maptiles/{style}/256/{zoom}/{x}/{y}?apikey={Uri.EscapeDataString(apiKey)}";

    private static Image<Rgba32> BlankTile() => new(256, 256, Color.White);
}
