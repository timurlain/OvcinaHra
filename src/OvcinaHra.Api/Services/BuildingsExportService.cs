using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Services.Pdf;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace OvcinaHra.Api.Services;

// Issue #512 — printable A4 building catalogue per game with Name / Effect /
// Recipe. Recipe is the per-game BuildingRecipe when it carries real content
// (ingredients / prerequisites / skills / notes), otherwise we fall back to
// Building.CostMoney as the price-only catalog stub. Sorted alphabetically.

public interface IBuildingsExportService
{
    Task<BuildingsExportFile> RenderBuildingsAsync(int gameId, CancellationToken ct = default);
}

public sealed record BuildingsExportFile(byte[] Bytes, string FileName);

public sealed class BuildingsExportProblemException(string detail)
    : Exception(detail)
{
    public string Title { get; } = "Export staveb se nepodařil";
    public string Detail { get; } = detail;
}

public sealed class BuildingsExportService(
    WorldDbContext db,
    IWebHostEnvironment environment,
    ILogger<BuildingsExportService> logger) : IBuildingsExportService
{
    private const double A4WidthPt = 595.276;
    private const double A4HeightPt = 841.89;
    private const int A4WidthPx = 2480;
    private const int A4HeightPx = 3508;
    private const int MarginPx = 80;
    private const int Columns = 2;
    private const int ColumnGapPx = 60;
    private const int CardGapPx = 24;
    private const int CardPaddingPx = 28;
    private const int CardBorderPx = 4;

    // Fixed-height card so flow-packing is trivial and predictable. 4 cards
    // per column × 2 columns = 8 buildings per page; long content is wrapped
    // with bounded lines and gracefully truncated if it would otherwise
    // overflow the box.
    private const int CardHeightPx = 800;
    private const int NameLines = 2;
    private const int EffectLines = 5;
    private const int RecipeLines = 8;

    private const float NameFontPx = 42;
    private const float SectionLabelFontPx = 22;
    private const float BodyFontPx = 28;

    private static readonly Color Paper = Color.White;
    private static readonly Color Ink = Color.Black;
    private static readonly Color MutedInk = Color.ParseHex("#404040");
    private static readonly Color SectionLabel = Color.ParseHex("#6B5A2B");
    private static readonly Color CardBg = Color.ParseHex("#FBF7EB");
    private static readonly Color CardBorder = Color.ParseHex("#C9BC9A");

    public async Task<BuildingsExportFile> RenderBuildingsAsync(int gameId, CancellationToken ct = default)
    {
        var totalStopwatch = Stopwatch.StartNew();
        logger.LogInformation("[export-server] buildings.entry gameId={GameId}", gameId);

        var game = await db.Games.AsNoTracking().FirstOrDefaultAsync(g => g.Id == gameId, ct);
        if (game is null)
        {
            logger.LogInformation("[export-server] buildings.exit status=404 gameId={GameId}", gameId);
            throw new KeyNotFoundException($"Game {gameId} not found.");
        }

        // Load each game-scoped Building together with the per-game recipe
        // (if any) so the recipe-resolution can pick the substantive one.
        var buildings = await db.GameBuildings
            .AsNoTracking()
            .Where(gb => gb.GameId == gameId)
            .Select(gb => new BuildingProjection(
                gb.Building.Id,
                gb.Building.Name,
                gb.Building.Effect,
                gb.Building.CostMoney,
                db.BuildingRecipes
                    .Where(r => r.GameId == gameId && r.OutputBuildingId == gb.BuildingId)
                    .Select(r => new RecipeProjection(
                        r.MoneyCost,
                        r.IngredientNotes,
                        r.Ingredients
                            .Select(i => new IngredientLine(i.Item.Name, i.Quantity))
                            .ToList(),
                        r.PrerequisiteBuildings
                            .Select(p => p.RequiredBuilding.Name)
                            .ToList(),
                        r.SkillRequirements
                            .Select(s => s.GameSkill.Name)
                            .ToList()))
                    .FirstOrDefault()))
            .ToListAsync(ct);

        if (buildings.Count == 0)
            throw new BuildingsExportProblemException("Vybraná hra nemá přiřazené žádné stavby.");

        var rows = buildings
            .OrderBy(b => b.Name, StringComparer.Create(new CultureInfo("cs-CZ"), ignoreCase: true))
            .Select(BuildRow)
            .ToList();

        var renderStopwatch = Stopwatch.StartNew();
        var fonts = LoadFonts();
        var rowsPerPage = ((A4HeightPx - MarginPx * 2 + CardGapPx) / (CardHeightPx + CardGapPx)) * Columns;
        var pageCount = Math.Max(1, (int)Math.Ceiling(rows.Count / (double)rowsPerPage));
        var pageImages = new List<PdfImagePage>(pageCount);

        for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            var pdfImage = await RenderPageAsync(rows, fonts, pageIndex, rowsPerPage, ct);
            pageImages.Add(new PdfImagePage(
                A4WidthPt,
                A4HeightPt,
                $"q {Pt(A4WidthPt)} 0 0 {Pt(A4HeightPt)} 0 0 cm /BuildingsPage{pageIndex} Do Q\n",
                new Dictionary<string, PdfImage> { [$"BuildingsPage{pageIndex}"] = pdfImage }));
        }

        var pdf = SimpleImagePdfWriter.Build(pageImages);
        var fileName = ExportFilenameBuilder.BuildExportFilename("Stavby", game.Name, includeDate: true);

        logger.LogInformation(
            "[export-server] buildings.render pages={Pages} buildings={Buildings} elapsedMs={ElapsedMs}",
            pageCount,
            rows.Count,
            renderStopwatch.ElapsedMilliseconds);
        logger.LogInformation(
            "[export-server] buildings.exit status=200 elapsedMs={ElapsedMs}",
            totalStopwatch.ElapsedMilliseconds);

        return new BuildingsExportFile(pdf, fileName);
    }

    private static BuildingRow BuildRow(BuildingProjection b)
    {
        var name = b.Name;
        var effect = string.IsNullOrWhiteSpace(b.Effect) ? "Bez efektu" : b.Effect;
        var recipe = b.Recipe;

        // "Use the one that actually is not empty for this game and contains
        // more than a price." Per-game recipe wins when it has structured
        // content; otherwise fall back to the catalog price stub.
        var hasSubstantiveRecipe = recipe is not null
            && (recipe.Ingredients.Count > 0
                || recipe.Prerequisites.Count > 0
                || recipe.SkillRequirements.Count > 0
                || !string.IsNullOrWhiteSpace(recipe.IngredientNotes));

        var sb = new StringBuilder();
        if (hasSubstantiveRecipe && recipe is not null)
        {
            if (recipe.Ingredients.Count > 0)
            {
                sb.Append("Suroviny: ");
                sb.AppendLine(string.Join(", ", recipe.Ingredients.Select(IngredientText)));
            }
            if (recipe.Prerequisites.Count > 0)
            {
                sb.Append("Stavby: ");
                sb.AppendLine(string.Join(", ", recipe.Prerequisites));
            }
            if (recipe.SkillRequirements.Count > 0)
            {
                sb.Append("Dovednosti: ");
                sb.AppendLine(string.Join(", ", recipe.SkillRequirements));
            }
            var price = recipe.MoneyCost ?? b.CostMoney;
            if (price is { } cost && cost > 0)
            {
                sb.Append("Cena: ");
                sb.Append(cost.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine(" grošů");
            }
            if (!string.IsNullOrWhiteSpace(recipe.IngredientNotes))
            {
                sb.AppendLine(recipe.IngredientNotes);
            }
        }
        else if (b.CostMoney is { } cost && cost > 0)
        {
            sb.Append("Cena: ");
            sb.Append(cost.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine(" grošů");
        }
        else
        {
            sb.AppendLine("Recept zatím není zaznamenán.");
        }

        return new BuildingRow(name, effect, sb.ToString().TrimEnd());
    }

    private static string IngredientText(IngredientLine i) =>
        i.Quantity <= 1
            ? i.Name
            : $"{i.Name} × {i.Quantity.ToString(CultureInfo.InvariantCulture)}";

    private async Task<PdfImage> RenderPageAsync(
        IReadOnlyList<BuildingRow> rows,
        BuildingsFonts fonts,
        int pageIndex,
        int rowsPerPage,
        CancellationToken ct)
    {
        using var image = new Image<Rgba32>(A4WidthPx, A4HeightPx, Paper);
        image.Mutate(ctx =>
        {
            var pageStart = pageIndex * rowsPerPage;
            var rowsPerColumn = rowsPerPage / Columns;
            var columnWidth = (A4WidthPx - MarginPx * 2 - ColumnGapPx) / Columns;

            for (var column = 0; column < Columns; column++)
            {
                var x = MarginPx + column * (columnWidth + ColumnGapPx);
                for (var row = 0; row < rowsPerColumn; row++)
                {
                    var index = pageStart + column * rowsPerColumn + row;
                    if (index >= rows.Count) return;
                    var y = MarginPx + row * (CardHeightPx + CardGapPx);
                    DrawBuildingCard(ctx, rows[index], x, y, columnWidth, fonts);
                }
            }
        });

        await using var stream = new MemoryStream();
        await image.SaveAsJpegAsync(stream, new JpegEncoder { Quality = 95 }, ct);
        return new PdfImage(A4WidthPx, A4HeightPx, stream.ToArray());
    }

    private static void DrawBuildingCard(
        IImageProcessingContext ctx,
        BuildingRow row,
        float x,
        float y,
        float width,
        BuildingsFonts fonts)
    {
        var rect = new RectangularPolygon(x, y, width, CardHeightPx);
        ctx.Fill(CardBg, rect);
        ctx.Draw(CardBorder, CardBorderPx, rect);

        var contentX = x + CardPaddingPx;
        var contentWidth = width - CardPaddingPx * 2;
        var cursorY = y + CardPaddingPx;

        // Name
        DrawWrappedText(ctx, row.Name, fonts.Name, Ink,
            contentX, cursorY, contentWidth, NameFontPx * 1.2f * NameLines, NameLines);
        cursorY += NameFontPx * 1.2f * NameLines + 12f;

        // Effect label
        ctx.DrawText("Efekt", fonts.SectionLabel, SectionLabel, new PointF(contentX, cursorY));
        cursorY += SectionLabelFontPx * 1.2f + 4f;

        // Effect body
        var effectHeight = BodyFontPx * 1.3f * EffectLines;
        DrawWrappedText(ctx, row.Effect, fonts.Body, MutedInk,
            contentX, cursorY, contentWidth, effectHeight, EffectLines);
        cursorY += effectHeight + 16f;

        // Recipe label
        ctx.DrawText("Recept", fonts.SectionLabel, SectionLabel, new PointF(contentX, cursorY));
        cursorY += SectionLabelFontPx * 1.2f + 4f;

        // Recipe body
        var recipeHeight = BodyFontPx * 1.3f * RecipeLines;
        DrawWrappedText(ctx, row.Recipe, fonts.Body, Ink,
            contentX, cursorY, contentWidth, recipeHeight, RecipeLines);
    }

    private static void DrawWrappedText(
        IImageProcessingContext ctx,
        string text,
        Font font,
        Color color,
        float x,
        float y,
        float width,
        float height,
        int maxLines)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var lines = WrapText(text, font, width, maxLines);
        var lineHeight = font.Size * 1.3f;
        for (var i = 0; i < lines.Count; i++)
        {
            ctx.DrawText(lines[i], font, color, new PointF(x, y + i * lineHeight));
        }
    }

    private static List<string> WrapText(string text, Font font, float maxWidth, int maxLines)
    {
        var lines = new List<string>();
        var paragraphs = text.Replace("\r\n", "\n").Split('\n');
        foreach (var paragraph in paragraphs)
        {
            if (lines.Count >= maxLines) break;
            if (string.IsNullOrWhiteSpace(paragraph)) { lines.Add(string.Empty); continue; }

            var words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var current = new StringBuilder();
            foreach (var word in words)
            {
                var candidate = current.Length == 0 ? word : current + " " + word;
                var size = TextMeasurer.MeasureSize(candidate, new TextOptions(font));
                if (size.Width <= maxWidth)
                {
                    current.Clear();
                    current.Append(candidate);
                }
                else
                {
                    if (current.Length > 0)
                    {
                        lines.Add(current.ToString());
                        if (lines.Count >= maxLines) break;
                        current.Clear();
                        current.Append(word);
                    }
                    else
                    {
                        // Single word longer than the column — keep it whole;
                        // ImageSharp will overflow visually but it beats
                        // dropping the word entirely.
                        lines.Add(word);
                        if (lines.Count >= maxLines) break;
                    }
                }
            }
            if (current.Length > 0 && lines.Count < maxLines) lines.Add(current.ToString());
        }

        // Append "…" to the last line if we ran out of room.
        if (lines.Count == maxLines)
        {
            var last = lines[^1];
            if (!last.EndsWith('…'))
            {
                while (last.Length > 0)
                {
                    var probe = last + "…";
                    var size = TextMeasurer.MeasureSize(probe, new TextOptions(font));
                    if (size.Width <= maxWidth)
                    {
                        lines[^1] = probe;
                        break;
                    }
                    last = last[..^1];
                    lines[^1] = last;
                }
            }
        }
        return lines;
    }

    private BuildingsFonts LoadFonts()
    {
        var fontRoot = System.IO.Path.Combine(environment.ContentRootPath, "Fonts", "Inter");
        try
        {
            var fontCollection = new FontCollection();
            var regular = fontCollection.Add(System.IO.Path.Combine(fontRoot, "Inter-Regular.ttf"));
            var bold = fontCollection.Add(System.IO.Path.Combine(fontRoot, "Inter-Bold.ttf"));
            return new BuildingsFonts(
                Name: bold.CreateFont(NameFontPx),
                SectionLabel: bold.CreateFont(SectionLabelFontPx),
                Body: regular.CreateFont(BodyFontPx));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[export-server] buildings.font-load-failed path={Path}", fontRoot);
            throw;
        }
    }

    private static string Pt(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private sealed record BuildingProjection(
        int Id,
        string Name,
        string? Effect,
        int? CostMoney,
        RecipeProjection? Recipe);

    private sealed record RecipeProjection(
        int? MoneyCost,
        string? IngredientNotes,
        List<IngredientLine> Ingredients,
        List<string> Prerequisites,
        List<string> SkillRequirements);

    private sealed record IngredientLine(string Name, int Quantity);

    private sealed record BuildingRow(string Name, string Effect, string Recipe);

    private sealed record BuildingsFonts(Font Name, Font SectionLabel, Font Body);
}
