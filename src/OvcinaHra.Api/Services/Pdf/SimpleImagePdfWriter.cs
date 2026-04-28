using System.Globalization;
using System.Text;

namespace OvcinaHra.Api.Services.Pdf;

public sealed record PdfImage(int Width, int Height, byte[] JpegBytes);

public sealed record PdfImagePage(
    double Width,
    double Height,
    string Content,
    IReadOnlyDictionary<string, PdfImage> Images);

public static class SimpleImagePdfWriter
{
    public static byte[] Build(double pageWidth, double pageHeight, string content, PdfImage? image) =>
        Build([
            new PdfImagePage(
                pageWidth,
                pageHeight,
                content,
                image is null
                    ? new Dictionary<string, PdfImage>()
                    : new Dictionary<string, PdfImage> { ["Im1"] = image })
        ]);

    public static byte[] Build(IReadOnlyList<PdfImagePage> pages)
    {
        ArgumentOutOfRangeException.ThrowIfZero(pages.Count);

        var objects = new List<byte[]?>
        {
            Ascii("<< /Type /Catalog /Pages 2 0 R >>"),
            null,
            Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"),
            Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>")
        };
        var pageObjectNumbers = new List<int>();

        foreach (var page in pages)
        {
            var imageObjectNumbers = new Dictionary<string, int>();
            foreach (var (name, image) in page.Images)
            {
                imageObjectNumbers[name] = objects.Count + 1;
                objects.Add(BuildImageObject(image));
            }

            var pageObjectNumber = objects.Count + 1;
            var contentObjectNumber = objects.Count + 2;
            pageObjectNumbers.Add(pageObjectNumber);
            objects.Add(Ascii(BuildPageObject(page, imageObjectNumbers, contentObjectNumber)));
            objects.Add(BuildStreamObject(Ascii(page.Content)));
        }

        objects[1] = Ascii(BuildPagesObject(pageObjectNumbers));
        return WriteObjects(objects.Select(o => o ?? throw new InvalidOperationException("PDF object was not built.")).ToList());
    }

    private static string BuildPagesObject(IReadOnlyList<int> pageObjectNumbers)
    {
        var kids = string.Join(" ", pageObjectNumbers.Select(n => $"{n} 0 R"));
        return $"<< /Type /Pages /Kids [ {kids} ] /Count {pageObjectNumbers.Count} >>";
    }

    private static string BuildPageObject(
        PdfImagePage page,
        IReadOnlyDictionary<string, int> imageObjectNumbers,
        int contentObjectNumber)
    {
        var xObjects = imageObjectNumbers.Count == 0
            ? string.Empty
            : " /XObject << "
                + string.Join(" ", imageObjectNumbers.Select(kvp => $"/{kvp.Key} {kvp.Value} 0 R"))
                + " >>";
        var resources = $"/Resources << /Font << /F1 3 0 R /F2 4 0 R >>{xObjects} >>";
        return $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {Pt(page.Width)} {Pt(page.Height)}] {resources} /Contents {contentObjectNumber} 0 R >>";
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
