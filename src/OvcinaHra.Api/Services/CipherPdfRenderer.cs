using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using OvcinaHra.Shared.Ciphers;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Extensions;

namespace OvcinaHra.Api.Services;

public interface ICipherPdfRenderer
{
    byte[] RenderSingle(CipherPdfCard card);
    byte[] RenderLocation(IReadOnlyList<CipherPdfCard> cards);
}

public sealed record CipherPdfCard(
    string LocationName,
    CipherSkillKey SkillKey,
    string MessageNormalized);

public sealed class CipherPdfRenderer(IWebHostEnvironment environment) : ICipherPdfRenderer
{
    private const double A4PortraitWidth = 210;
    private const double A4PortraitHeight = 297;
    private const double A4LandscapeWidth = 297;
    private const double A4LandscapeHeight = 210;
    private const double CardWidth = 148;
    private const double CardHeight = 210;
    private const int GridColumns = 22;
    private const int GridRows = 25;
    private const double GridX = 8;
    private const double GridY = 35;
    private const double CellSize = 6;
    private const double ActiveCellPadding = 0.3;

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static readonly Regex NumberRegex = new(@"-?\d+(?:\.\d+)?", RegexOptions.Compiled);

    private static readonly IReadOnlyDictionary<CipherSkillKey, string> FillerText =
        new Dictionary<CipherSkillKey, string>
        {
            [CipherSkillKey.HledaniMagie] = "MAGIEKAMENLESPLAMENHVEZDARUNA",
            [CipherSkillKey.Prohledavani] = "STOPAKORENKLICLUCERNAKAPSASTIN",
            [CipherSkillKey.SestySmysl] = "TICHOSENSTINHLASDUCHVETRKROK",
            [CipherSkillKey.ZnalostBytosti] = "BESTIARSTVOPOTVORAKOSTTESAK",
            [CipherSkillKey.Lezeni] = "SKALALANOHAAKSTENAKOPECSMYCKA"
        };

    private readonly ConcurrentDictionary<CipherSkillKey, OverlayGeometry> overlayCache = new();

    public byte[] RenderSingle(CipherPdfCard card)
    {
        var page = new PdfPage(A4PortraitWidth, A4PortraitHeight);
        RenderCard(page.Content, page.Height, card, xOffset: 31, yOffset: 20);
        DrawCalibrationStrip(page.Content, page.Height);
        return SimplePdfWriter.Build([page]);
    }

    public byte[] RenderLocation(IReadOnlyList<CipherPdfCard> cards)
    {
        if (cards.Count == 0)
            throw new ArgumentException("At least one cipher card is required.", nameof(cards));

        var pages = new List<PdfPage>();
        for (var i = 0; i < cards.Count; i += 2)
        {
            var page = new PdfPage(A4LandscapeWidth, A4LandscapeHeight);
            if (i + 1 < cards.Count)
            {
                RenderCard(page.Content, page.Height, cards[i], xOffset: 0, yOffset: 0);
                RenderCard(page.Content, page.Height, cards[i + 1], xOffset: 149, yOffset: 0);
            }
            else
            {
                RenderCard(page.Content, page.Height, cards[i], xOffset: (A4LandscapeWidth - CardWidth) / 2, yOffset: 0);
            }

            pages.Add(page);
        }

        return SimplePdfWriter.Build(pages);
    }

    private void RenderCard(StringBuilder content, double pageHeight, CipherPdfCard card, double xOffset, double yOffset)
    {
        var overlay = overlayCache.GetOrAdd(card.SkillKey, LoadOverlay);
        var grid = BuildGrid(card, overlay.ActiveCells);

        FillRectangle(content, pageHeight, xOffset, yOffset, CardWidth, CardHeight, "1 g");
        foreach (var line in overlay.Lines)
        {
            StrokeLine(
                content,
                pageHeight,
                xOffset + line.X1,
                yOffset + line.Y1,
                xOffset + line.X2,
                yOffset + line.Y2,
                line.StrokeWidth,
                line.IsLight ? "0.93 G" : "0 G");
        }

        foreach (var rect in overlay.ActiveRects)
            FillRectangle(content, pageHeight, xOffset + rect.X, yOffset + rect.Y, rect.Width, rect.Height, "0 g");

        DrawCenteredText(content, pageHeight, ToHeaderText(card.LocationName), xOffset + 74, yOffset + 14, 8, "F1", "0 g");
        DrawCenteredText(content, pageHeight, ToHeaderText(card.SkillKey.GetDisplayName()), xOffset + 74, yOffset + 25, 10, "F2", "0 g");

        var active = overlay.ActiveCells.ToHashSet();
        for (var row = 0; row < GridRows; row++)
        {
            for (var column = 0; column < GridColumns; column++)
            {
                var cell = new GridCell(row, column);
                var isActive = active.Contains(cell);
                var letter = grid[row, column].ToString();
                var centerX = xOffset + GridX + column * CellSize + CellSize / 2;
                var baselineY = yOffset + GridY + row * CellSize + CellSize / 2 + 1.4;
                DrawCenteredText(
                    content,
                    pageHeight,
                    letter,
                    centerX,
                    baselineY,
                    10,
                    "F2",
                    isActive ? "1 g" : "0.15 g");
            }
        }
    }

    private OverlayGeometry LoadOverlay(CipherSkillKey skillKey)
    {
        var path = Path.Combine(
            environment.ContentRootPath,
            "Ciphers",
            "Overlays",
            $"test-overlay-{skillKey.GetSlug()}.svg");
        var doc = XDocument.Load(path);

        var lines = doc.Descendants()
            .Where(e => e.Name.LocalName == "line")
            .Select(e => new SvgLine(
                ReadDouble(e, "x1"),
                ReadDouble(e, "y1"),
                ReadDouble(e, "x2"),
                ReadDouble(e, "y2"),
                ReadOptionalDouble(e, "stroke-width", 0.1),
                string.Equals((string?)e.Attribute("stroke"), "#eeeeee", StringComparison.OrdinalIgnoreCase)))
            .Concat(doc.Descendants()
                .Where(e => e.Name.LocalName == "path")
                .SelectMany(ParsePathLines))
            .ToArray();

        var activeRects = doc.Descendants()
            .Where(e => e.Name.LocalName == "rect"
                && string.Equals((string?)e.Attribute("fill"), "black", StringComparison.OrdinalIgnoreCase))
            .Select(e => new SvgRect(
                ReadDouble(e, "x"),
                ReadDouble(e, "y"),
                ReadDouble(e, "width"),
                ReadDouble(e, "height")))
            .ToArray();

        var activeCells = activeRects
            .Select(rect => new GridCell(
                Row: (int)Math.Round((rect.Y - GridY - ActiveCellPadding) / CellSize),
                Column: (int)Math.Round((rect.X - GridX - ActiveCellPadding) / CellSize)))
            .Distinct()
            .OrderBy(cell => cell.Row)
            .ThenBy(cell => cell.Column)
            .ToArray();

        return new OverlayGeometry(lines, activeRects, activeCells);
    }

    private static IEnumerable<SvgLine> ParsePathLines(XElement pathElement)
    {
        var path = (string?)pathElement.Attribute("d") ?? "";
        var numbers = NumberRegex.Matches(path)
            .Select(match => double.Parse(match.Value, Invariant))
            .ToArray();

        for (var i = 0; i + 3 < numbers.Length; i += 2)
        {
            yield return new SvgLine(
                numbers[i],
                numbers[i + 1],
                numbers[i + 2],
                numbers[i + 3],
                ReadOptionalDouble(pathElement, "stroke-width", 0.5),
                IsLight: false);
        }
    }

    private static char[,] BuildGrid(CipherPdfCard card, IReadOnlyList<GridCell> activeCells)
    {
        var filler = CipherTextNormalizer.NormalizeMessage(FillerText[card.SkillKey]);
        var grid = new char[GridRows, GridColumns];
        for (var row = 0; row < GridRows; row++)
        {
            for (var column = 0; column < GridColumns; column++)
                grid[row, column] = filler[(row * GridColumns + column) % filler.Length];
        }

        var encoded = $"XOX{CipherTextNormalizer.NormalizeMessage(card.MessageNormalized)}XOX";
        if (encoded.Length > activeCells.Count)
            throw new InvalidOperationException(
                $"Cipher message for {card.SkillKey} needs {encoded.Length} cells, but the overlay contains {activeCells.Count}.");

        for (var i = 0; i < encoded.Length; i++)
        {
            var cell = activeCells[i];
            grid[cell.Row, cell.Column] = encoded[i];
        }

        return grid;
    }

    private static void DrawCalibrationStrip(StringBuilder content, double pageHeight)
    {
        const double startX = 55;
        const double endX = 155;
        const double y = 266;
        StrokeLine(content, pageHeight, startX, y, endX, y, 0.35, "0 G");
        StrokeLine(content, pageHeight, startX, y - 3, startX, y + 3, 0.35, "0 G");
        StrokeLine(content, pageHeight, endX, y - 3, endX, y + 3, 0.35, "0 G");
        DrawCenteredText(content, pageHeight, "100 MM", (startX + endX) / 2, y + 8, 9, "F1", "0 g");
    }

    private static void DrawCenteredText(
        StringBuilder content,
        double pageHeight,
        string text,
        double centerX,
        double baselineY,
        double fontSizePt,
        string fontName,
        string fillColor)
    {
        var textWidthMm = text.Length * fontSizePt * 0.176;
        var x = centerX - textWidthMm / 2;
        content.AppendLine($"{fillColor} BT /{fontName} {Format(fontSizePt)} Tf {Pt(x)} {Pt(pageHeight - baselineY)} Td ({EscapePdfString(text)}) Tj ET");
    }

    private static void StrokeLine(
        StringBuilder content,
        double pageHeight,
        double x1,
        double y1,
        double x2,
        double y2,
        double strokeWidth,
        string strokeColor)
    {
        content.AppendLine($"{strokeColor} {Pt(strokeWidth)} w {Pt(x1)} {Pt(pageHeight - y1)} m {Pt(x2)} {Pt(pageHeight - y2)} l S");
    }

    private static void FillRectangle(
        StringBuilder content,
        double pageHeight,
        double x,
        double y,
        double width,
        double height,
        string fillColor)
    {
        content.AppendLine($"{fillColor} {Pt(x)} {Pt(pageHeight - y - height)} {Pt(width)} {Pt(height)} re f");
    }

    private static string ToHeaderText(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var previousWasSpace = false;

        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;

            var upper = char.ToUpperInvariant(ch);
            if (upper is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                builder.Append(upper);
                previousWasSpace = false;
            }
            else if ((upper == ' ' || upper == '-' || upper == '/') && !previousWasSpace)
            {
                builder.Append(' ');
                previousWasSpace = true;
            }
        }

        var result = builder.ToString().Trim();
        return result.Length <= 38 ? result : result[..38];
    }

    private static double ReadDouble(XElement element, string name) =>
        double.Parse((string?)element.Attribute(name) ?? "0", Invariant);

    private static double ReadOptionalDouble(XElement element, string name, double fallback) =>
        double.TryParse((string?)element.Attribute(name), NumberStyles.Float, Invariant, out var value)
            ? value
            : fallback;

    private static string Pt(double millimeters) => Format(millimeters * 72 / 25.4);

    private static string Format(double value) => value.ToString("0.###", Invariant);

    private static string EscapePdfString(string value) =>
        value.Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("(", @"\(", StringComparison.Ordinal)
            .Replace(")", @"\)", StringComparison.Ordinal);

    private readonly record struct GridCell(int Row, int Column);

    private readonly record struct SvgLine(
        double X1,
        double Y1,
        double X2,
        double Y2,
        double StrokeWidth,
        bool IsLight);

    private readonly record struct SvgRect(double X, double Y, double Width, double Height);

    private sealed record OverlayGeometry(
        IReadOnlyList<SvgLine> Lines,
        IReadOnlyList<SvgRect> ActiveRects,
        IReadOnlyList<GridCell> ActiveCells);

    private sealed record PdfPage(double Width, double Height)
    {
        public StringBuilder Content { get; } = new();
    }

    private static class SimplePdfWriter
    {
        public static byte[] Build(IReadOnlyList<PdfPage> pages)
        {
            var objects = new List<string>
            {
                "<< /Type /Catalog /Pages 2 0 R >>",
                BuildPagesObject(pages),
                "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
                "<< /Type /Font /Subtype /Type1 /BaseFont /Courier-Bold >>"
            };

            for (var i = 0; i < pages.Count; i++)
            {
                var pageObjectNumber = 5 + i * 2;
                var contentObjectNumber = pageObjectNumber + 1;
                var content = pages[i].Content.ToString();
                objects.Add(BuildPageObject(pages[i], contentObjectNumber));
                objects.Add(BuildStreamObject(content));
            }

            return WriteObjects(objects);
        }

        private static string BuildPagesObject(IReadOnlyList<PdfPage> pages)
        {
            var kids = string.Join(" ", Enumerable.Range(0, pages.Count).Select(i => $"{5 + i * 2} 0 R"));
            return $"<< /Type /Pages /Kids [ {kids} ] /Count {pages.Count} >>";
        }

        private static string BuildPageObject(PdfPage page, int contentObjectNumber) =>
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {Pt(page.Width)} {Pt(page.Height)}] "
            + $"/Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {contentObjectNumber} 0 R >>";

        private static string BuildStreamObject(string content) =>
            $"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}endstream";

        private static byte[] WriteObjects(IReadOnlyList<string> objects)
        {
            using var stream = new MemoryStream();
            WriteAscii(stream, "%PDF-1.4\n");
            var offsets = new long[objects.Count + 1];

            for (var i = 0; i < objects.Count; i++)
            {
                offsets[i + 1] = stream.Position;
                WriteAscii(stream, $"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
            }

            var xrefOffset = stream.Position;
            WriteAscii(stream, $"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
            for (var i = 1; i < offsets.Length; i++)
                WriteAscii(stream, $"{offsets[i]:D10} 00000 n \n");
            WriteAscii(stream, $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");

            return stream.ToArray();
        }

        private static void WriteAscii(Stream stream, string value)
        {
            var bytes = Encoding.ASCII.GetBytes(value);
            stream.Write(bytes);
        }
    }
}
