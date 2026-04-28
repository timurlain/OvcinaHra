using Microsoft.EntityFrameworkCore;
using OvcinaHra.Api.Data;
using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Api.Services;

public interface IMagicBookExportPlanner
{
    Task<MagicBookDocument?> BuildAsync(int gameId, CancellationToken cancellationToken = default);
}

public sealed class MagicBookExportPlanner(WorldDbContext db) : IMagicBookExportPlanner
{
    public const double A4WidthMm = 210;
    public const double A4HeightMm = 297;
    public const double A6WidthMm = 105;
    public const double A6HeightMm = 148;
    public const double SafeMarginMm = 5;

    private static readonly int[] LowLevelPage = [1, 2, 3];
    private static readonly int[] HighLevelPage = [4, 5];

    private static readonly IReadOnlyDictionary<int, string> LevelColors = new Dictionary<int, string>
    {
        [1] = "#ffff00",
        [2] = "#92d050",
        [3] = "#ff0000",
        [4] = "#8eaadb",
        [5] = "#7030a0"
    };

    public async Task<MagicBookDocument?> BuildAsync(int gameId, CancellationToken cancellationToken = default)
    {
        var game = await db.Games
            .AsNoTracking()
            .Where(g => g.Id == gameId)
            .Select(g => new { g.Id, g.Name, g.Edition })
            .FirstOrDefaultAsync(cancellationToken);

        if (game is null)
            return null;

        var spells = await db.GameSpells
            .AsNoTracking()
            .Where(gs => gs.GameId == gameId)
            .OrderBy(gs => gs.Spell.Level)
            .ThenBy(gs => gs.Spell.Name)
            .Select(gs => new MagicBookSpell(
                gs.Id,
                gs.SpellId,
                gs.Spell.Name,
                gs.Spell.Level,
                gs.Spell.School,
                gs.Spell.ManaCost,
                gs.Spell.MinMageLevel,
                gs.Price ?? gs.Spell.Price,
                gs.Spell.Effect,
                gs.Spell.Description,
                gs.Spell.IsReaction,
                gs.IsFindable,
                gs.AvailabilityNotes))
            .ToListAsync(cancellationToken);

        return new MagicBookDocument(
            game.Id,
            game.Name,
            game.Edition,
            spells.Count,
            [
                BuildPage(1, "Úrovně I-III", LowLevelPage, spells),
                BuildPage(2, "Úrovně IV-V", HighLevelPage, spells)
            ],
            MagicBookImpositionPlan.TwoUpA4Duplex());
    }

    private static MagicBookPage BuildPage(
        int pageNumber,
        string title,
        IReadOnlyList<int> levels,
        IReadOnlyList<MagicBookSpell> spells)
    {
        var sections = levels
            .Select(level => new MagicBookLevelSection(
                level,
                LevelRoman(level),
                LevelColors[level],
                spells.Where(s => s.Level == level).ToList()))
            .ToList();

        return new MagicBookPage(pageNumber, title, sections);
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
}

public sealed record MagicBookDocument(
    int GameId,
    string GameName,
    int GameEdition,
    int SpellCount,
    IReadOnlyList<MagicBookPage> Pages,
    MagicBookImpositionPlan Imposition);

public sealed record MagicBookPage(
    int PageNumber,
    string Title,
    IReadOnlyList<MagicBookLevelSection> Sections);

public sealed record MagicBookLevelSection(
    int Level,
    string LevelRoman,
    string ColorHex,
    IReadOnlyList<MagicBookSpell> Spells);

public sealed record MagicBookSpell(
    int GameSpellId,
    int SpellId,
    string Name,
    int Level,
    SpellSchool School,
    int ManaCost,
    int MinMageLevel,
    int? EffectivePrice,
    string Effect,
    string? Description,
    bool IsReaction,
    bool IsFindable,
    string? AvailabilityNotes);

public sealed record MagicBookImpositionPlan(
    double SheetWidthMm,
    double SheetHeightMm,
    double SlotWidthMm,
    double SlotHeightMm,
    double SafeMarginMm,
    IReadOnlyList<MagicBookImpositionSheet> Sheets)
{
    public static MagicBookImpositionPlan TwoUpA4Duplex() => new(
        MagicBookExportPlanner.A4WidthMm,
        MagicBookExportPlanner.A4HeightMm,
        MagicBookExportPlanner.A6WidthMm,
        MagicBookExportPlanner.A6HeightMm,
        MagicBookExportPlanner.SafeMarginMm,
        [
            new MagicBookImpositionSheet(
                1,
                "front",
                [
                    new MagicBookImpositionSlot(1, 1, CenteredA6X, 0),
                    new MagicBookImpositionSlot(2, 1, CenteredA6X, A4HalfY)
                ]),
            new MagicBookImpositionSheet(
                2,
                "back",
                [
                    new MagicBookImpositionSlot(1, 2, CenteredA6X, 0),
                    new MagicBookImpositionSlot(2, 2, CenteredA6X, A4HalfY)
                ])
        ]);

    private const double CenteredA6X = (MagicBookExportPlanner.A4WidthMm - MagicBookExportPlanner.A6WidthMm) / 2;
    private const double A4HalfY = MagicBookExportPlanner.A4HeightMm / 2;
}

public sealed record MagicBookImpositionSheet(
    int SheetNumber,
    string Side,
    IReadOnlyList<MagicBookImpositionSlot> Slots);

public sealed record MagicBookImpositionSlot(
    int CopyNumber,
    int MagicBookPageNumber,
    double XMm,
    double YMm);
