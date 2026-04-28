using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using OvcinaHra.Api.Data;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace OvcinaHra.Api.Services;

public interface IExplorerMapExportService
{
    Task<ExplorerMapExportFile> RenderExplorerMapAsync(
        int gameId,
        MapExportBasemapStyle style,
        CancellationToken ct = default);
}

public sealed record ExplorerMapExportFile(byte[] Bytes, string FileName);

public sealed class MapExportProblemException(string detail) : Exception(detail);

public sealed class ExplorerMapExportService(
    WorldDbContext db,
    IMapDataService mapData,
    IMapTileClient tileClient,
    ILogger<ExplorerMapExportService> logger) : IExplorerMapExportService
{
    private const double A4Width = 595.276;
    private const double A4Height = 841.89;
    private const double PageMargin = 24;
    private const double PixelScale = 2.5;

    private static readonly JsonSerializerOptions OverlayJsonOptions = new() { WriteIndented = false };

    public async Task<ExplorerMapExportFile> RenderExplorerMapAsync(
        int gameId,
        MapExportBasemapStyle style,
        CancellationToken ct = default)
    {
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
                g.BoundingBoxNeLng,
                g.OverlayJson
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

        var layout = ExportLayout.For(A4Width, A4Height, bounds.AspectRatio);
        var mapImage = await RenderBasemapAsync(style, bounds, layout, ct);
        var overlay = DeserializeOverlay(game.OverlayJson);

        var canvas = new PdfCanvas(A4Height);
        canvas.FillRectangle(0, 0, A4Width, A4Height, "#ffffff");
        canvas.FillRectangle(layout.MapX, layout.MapY, layout.MapWidth, layout.MapHeight, "#f8f5ed");
        canvas.BeginClip(layout.MapX, layout.MapY, layout.MapWidth, layout.MapHeight);
        if (mapImage is not null)
            canvas.DrawImage("Im1", layout.MapX, layout.MapY, layout.MapWidth, layout.MapHeight);
        RenderOverlay(canvas, layout, bounds, overlay);
        RenderPins(canvas, layout, bounds, locations);
        canvas.EndClip();
        canvas.StrokeRectangle(layout.MapX, layout.MapY, layout.MapWidth, layout.MapHeight, 0.8, "#6f6658");

        var pdf = SimpleImagePdfWriter.Build(
            A4Width,
            A4Height,
            canvas.Content.ToString(),
            mapImage);

        var fileName = $"{Slugify(game.Name)}-postavy-{DateTime.Today:yyyy-MM-dd}.pdf";
        return new ExplorerMapExportFile(pdf, fileName);
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

        var tilesToDraw = new List<(Image<Rgba32> Tile, int X, int Y)>();
        try
        {
            for (var tx = minTileX; tx <= maxTileX; tx++)
            {
                for (var ty = minTileY; ty <= maxTileY; ty++)
                {
                    ct.ThrowIfCancellationRequested();
                    var tile = await tileClient.GetTileAsync(style, zoom, tx, ty, ct);
                    var x = (int)Math.Round(tx * 256 - westPx);
                    var y = (int)Math.Round(ty * 256 - northPx);
                    tilesToDraw.Add((tile, x, y));
                }
            }

            stitched.Mutate(ctx =>
            {
                foreach (var (tile, x, y) in tilesToDraw)
                    ctx.DrawImage(tile, new Point(x, y), 1f);

                ctx.Resize(width, height);
            });
        }
        finally
        {
            foreach (var (tile, _, _) in tilesToDraw)
                tile.Dispose();
        }

        await using var stream = new MemoryStream();
        await stitched.SaveAsJpegAsync(stream, new JpegEncoder { Quality = 88 }, ct);
        logger.LogInformation(
            "Rendered explorer map basemap style={Style} zoom={Zoom} tiles={TileCount}",
            style,
            zoom,
            (maxTileX - minTileX + 1) * (maxTileY - minTileY + 1));
        return new PdfImage(width, height, stream.ToArray());
    }

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
        IReadOnlyList<MapLocationDto> locations)
    {
        foreach (var location in locations)
        {
            var point = layout.Project(bounds, location.Lat, location.Lon);
            var kind = location.EffectiveKind;
            var showName = kind is LocationKind.Town or LocationKind.Village;
            DrawPin(canvas, point.X, point.Y, kind, showName ? null : location.Id.ToString(CultureInfo.InvariantCulture));

            if (showName)
            {
                canvas.DrawLabel(
                    NormalizePdfText(location.EffectiveName, 34),
                    point.X + 10,
                    point.Y - 17,
                    8.5,
                    "#1f1a12",
                    "#ffffff");
            }
        }
    }

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

    private static string Slugify(string value)
    {
        var normalized = NormalizePdfText(value, 80).ToLowerInvariant();
        var builder = new StringBuilder(normalized.Length);
        var previousDash = false;

        foreach (var ch in normalized)
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(ch);
                previousDash = false;
            }
            else if (!previousDash)
            {
                builder.Append('-');
                previousDash = true;
            }
        }

        var result = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(result) ? "ovcina" : result;
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

    private sealed record PdfImage(int Width, int Height, byte[] JpegBytes);

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

    private static class SimpleImagePdfWriter
    {
        public static byte[] Build(double pageWidth, double pageHeight, string content, PdfImage? image)
        {
            var pageObjectNumber = image is null ? 5 : 6;
            var contentObjectNumber = image is null ? 6 : 7;
            var objects = new List<byte[]>
            {
                Ascii("<< /Type /Catalog /Pages 2 0 R >>"),
                Ascii($"<< /Type /Pages /Kids [ {pageObjectNumber} 0 R ] /Count 1 >>"),
                Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"),
                Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>")
            };

            if (image is not null)
                objects.Add(BuildImageObject(image));

            var resources = image is null
                ? "/Resources << /Font << /F1 3 0 R /F2 4 0 R >> >>"
                : "/Resources << /Font << /F1 3 0 R /F2 4 0 R >> /XObject << /Im1 5 0 R >> >>";
            objects.Add(Ascii($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {Pt(pageWidth)} {Pt(pageHeight)}] {resources} /Contents {contentObjectNumber} 0 R >>"));
            objects.Add(BuildStreamObject(Ascii(content)));

            return WriteObjects(objects);
        }

        private static byte[] BuildImageObject(PdfImage image)
        {
            var header = Ascii($"<< /Type /XObject /Subtype /Image /Width {image.Width} /Height {image.Height} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {image.JpegBytes.Length} >>\nstream\n");
            var footer = Ascii("\nendstream");
            return Concat(header, image.JpegBytes, footer);
        }

        private static byte[] BuildStreamObject(byte[] stream)
        {
            var header = Ascii($"<< /Length {stream.Length} >>\nstream\n");
            var footer = Ascii("endstream");
            return Concat(header, stream, footer);
        }

        private static byte[] WriteObjects(IReadOnlyList<byte[]> objects)
        {
            using var stream = new MemoryStream();
            WriteAscii(stream, "%PDF-1.4\n");
            var offsets = new long[objects.Count + 1];

            for (var i = 0; i < objects.Count; i++)
            {
                offsets[i + 1] = stream.Position;
                WriteAscii(stream, $"{i + 1} 0 obj\n");
                stream.Write(objects[i]);
                WriteAscii(stream, "\nendobj\n");
            }

            var xrefOffset = stream.Position;
            WriteAscii(stream, $"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
            for (var i = 1; i < offsets.Length; i++)
                WriteAscii(stream, $"{offsets[i]:D10} 00000 n \n");
            WriteAscii(stream, $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");

            return stream.ToArray();
        }

        private static byte[] Concat(params byte[][] parts)
        {
            var length = parts.Sum(p => p.Length);
            var result = new byte[length];
            var offset = 0;
            foreach (var part in parts)
            {
                Buffer.BlockCopy(part, 0, result, offset, part.Length);
                offset += part.Length;
            }

            return result;
        }

        private static void WriteAscii(Stream stream, string value) => stream.Write(Ascii(value));
        private static byte[] Ascii(string value) => Encoding.ASCII.GetBytes(value);
        private static string Pt(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
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
        var url = BuildUrl(style, zoom, tileX, tileY);
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

    private string? BuildUrl(MapExportBasemapStyle style, int zoom, int x, int y)
    {
        return style switch
        {
            MapExportBasemapStyle.Tourist => BuildMapyUrl("outdoor", zoom, x, y),
            MapExportBasemapStyle.Aerial => BuildMapyUrl("aerial", zoom, x, y),
            MapExportBasemapStyle.Basic => BuildMapyUrl("basic", zoom, x, y),
            MapExportBasemapStyle.Osm => $"https://tile.openstreetmap.org/{zoom}/{x}/{y}.png",
            MapExportBasemapStyle.Blank => null,
            _ => null
        };
    }

    private string? BuildMapyUrl(string style, int zoom, int x, int y)
    {
        var apiKey = config["MapyCz:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogWarning("Mapy.cz tile requested but MapyCz:ApiKey is not configured.");
            return null;
        }

        return $"https://api.mapy.cz/v1/maptiles/{style}/256/{zoom}/{x}/{y}?apikey={Uri.EscapeDataString(apiKey)}";
    }

    private static Image<Rgba32> BlankTile() => new(256, 256, Color.White);
}
