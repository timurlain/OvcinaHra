using Microsoft.Extensions.DependencyInjection;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Services;
using OvcinaHra.Api.Tests.Fixtures;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Api.Tests.Services;

public class MagicBookExportPlannerTests(PostgresFixture postgres)
    : IntegrationTestBase(postgres), IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task BuildAsync_MissingGame_ReturnsNull()
    {
        using var scope = Factory.Services.CreateScope();
        var planner = scope.ServiceProvider.GetRequiredService<IMagicBookExportPlanner>();

        var document = await planner.BuildAsync(999_999);

        Assert.Null(document);
    }

    [Fact]
    public async Task BuildAsync_UsesOnlyGameSpellRows()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
        var planner = scope.ServiceProvider.GetRequiredService<IMagicBookExportPlanner>();

        var targetGame = CreateGame("Hra s knihou", 30);
        var otherGame = CreateGame("Jiná hra", 31);
        var targetSpell = CreateSpell("Kouzlo pro knihu", 1);
        var otherGameSpell = CreateSpell("Kouzlo jiné hry", 2);
        var catalogOnlySpell = CreateSpell("Jen v katalogu", 3);

        db.Games.AddRange(targetGame, otherGame);
        db.Spells.AddRange(targetSpell, otherGameSpell, catalogOnlySpell);
        await db.SaveChangesAsync();

        db.GameSpells.AddRange(
            new GameSpell { GameId = targetGame.Id, SpellId = targetSpell.Id },
            new GameSpell { GameId = otherGame.Id, SpellId = otherGameSpell.Id });
        await db.SaveChangesAsync();

        var document = await planner.BuildAsync(targetGame.Id);

        Assert.NotNull(document);
        Assert.Equal(1, document.SpellCount);
        Assert.Equal(new[] { "Kouzlo pro knihu" }, FlattenSpellNames(document));
    }

    [Fact]
    public async Task BuildAsync_GroupsIntoTwoConfirmedA6Pages()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
        var planner = scope.ServiceProvider.GetRequiredService<IMagicBookExportPlanner>();

        var game = CreateGame("Dvě A6 stránky", 30);
        var spells = Enumerable.Range(1, 5)
            .Select(level => CreateSpell($"Kouzlo {level}", level))
            .ToList();

        db.Games.Add(game);
        db.Spells.AddRange(spells);
        await db.SaveChangesAsync();

        db.GameSpells.AddRange(spells.Select(spell => new GameSpell { GameId = game.Id, SpellId = spell.Id }));
        await db.SaveChangesAsync();

        var document = await planner.BuildAsync(game.Id);

        Assert.NotNull(document);
        Assert.Equal(2, document.Pages.Count);
        Assert.Equal("Úrovně I-III", document.Pages[0].Title);
        Assert.Equal(new[] { 1, 2, 3 }, document.Pages[0].Sections.Select(s => s.Level));
        Assert.Equal("Úrovně IV-V", document.Pages[1].Title);
        Assert.Equal(new[] { 4, 5 }, document.Pages[1].Sections.Select(s => s.Level));
        Assert.Equal(new[] { "#ffff00", "#92d050", "#ff0000", "#8eaadb", "#7030a0" },
            document.Pages.SelectMany(p => p.Sections).Select(s => s.ColorHex));
    }

    [Fact]
    public async Task BuildAsync_KeepsGameSpellRulesAndSpellBody()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
        var planner = scope.ServiceProvider.GetRequiredService<IMagicBookExportPlanner>();

        var game = CreateGame("Pravidla hry", 30);
        var overridePrice = CreateSpell("Přepsaná cena", 1, price: 100);
        var inheritedPrice = CreateSpell("Zděděná cena", 2, price: 200);

        db.Games.Add(game);
        db.Spells.AddRange(overridePrice, inheritedPrice);
        await db.SaveChangesAsync();

        db.GameSpells.AddRange(
            new GameSpell
            {
                GameId = game.Id,
                SpellId = overridePrice.Id,
                Price = 33,
                IsFindable = true,
                AvailabilityNotes = "Pouze u mudrce"
            },
            new GameSpell
            {
                GameId = game.Id,
                SpellId = inheritedPrice.Id
            });
        await db.SaveChangesAsync();

        var document = await planner.BuildAsync(game.Id);

        Assert.NotNull(document);
        var first = FlattenSpells(document).Single(s => s.Name == "Přepsaná cena");
        Assert.Equal(33, first.EffectivePrice);
        Assert.True(first.IsFindable);
        Assert.Equal("Pouze u mudrce", first.AvailabilityNotes);
        Assert.Equal("Efekt Přepsaná cena", first.Effect);
        Assert.Equal("Popis Přepsaná cena", first.Description);

        var second = FlattenSpells(document).Single(s => s.Name == "Zděděná cena");
        Assert.Equal(200, second.EffectivePrice);
    }

    [Fact]
    public async Task BuildAsync_CarriesTwoUpA4DuplexPlan()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
        var planner = scope.ServiceProvider.GetRequiredService<IMagicBookExportPlanner>();

        var game = CreateGame("Impozice", 30);
        var spell = CreateSpell("Jiskra", 1);
        db.Games.Add(game);
        db.Spells.Add(spell);
        await db.SaveChangesAsync();

        db.GameSpells.Add(new GameSpell { GameId = game.Id, SpellId = spell.Id });
        await db.SaveChangesAsync();

        var document = await planner.BuildAsync(game.Id);

        Assert.NotNull(document);
        Assert.Equal(210, document.Imposition.SheetWidthMm);
        Assert.Equal(297, document.Imposition.SheetHeightMm);
        Assert.Equal(105, document.Imposition.SlotWidthMm);
        Assert.Equal(148, document.Imposition.SlotHeightMm);
        Assert.Equal(2, document.Imposition.Sheets.Count);
        Assert.All(document.Imposition.Sheets, sheet => Assert.Equal(2, sheet.Slots.Count));
        Assert.All(document.Imposition.Sheets[0].Slots, slot => Assert.Equal(1, slot.MagicBookPageNumber));
        Assert.All(document.Imposition.Sheets[1].Slots, slot => Assert.Equal(2, slot.MagicBookPageNumber));
    }

    private static Game CreateGame(string name, int edition) => new()
    {
        Name = name,
        Edition = edition,
        StartDate = new DateOnly(2026, 5, 1),
        EndDate = new DateOnly(2026, 5, 3),
        Status = GameStatus.Active
    };

    private static Spell CreateSpell(string name, int level, int? price = null) => new()
    {
        Name = name,
        Level = level,
        ManaCost = level,
        School = SpellSchool.Fire,
        IsScroll = false,
        IsReaction = false,
        IsLearnable = true,
        MinMageLevel = level,
        Price = price,
        Effect = $"Efekt {name}",
        Description = $"Popis {name}"
    };

    private static List<string> FlattenSpellNames(MagicBookDocument document) =>
        FlattenSpells(document).Select(s => s.Name).ToList();

    private static List<MagicBookSpell> FlattenSpells(MagicBookDocument document) =>
        document.Pages
            .SelectMany(p => p.Sections)
            .SelectMany(s => s.Spells)
            .ToList();
}
