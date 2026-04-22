using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Tests.Fixtures;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Tests.Endpoints;

public class SpellEndpointTests(PostgresFixture postgres) : IntegrationTestBase(postgres)
{
    [Fact]
    public async Task Create_ValidSpell_ReturnsCreated()
    {
        var dto = new CreateSpellDto(
            Name: "Ohnivá střela",
            Level: 1,
            ManaCost: 1,
            School: SpellSchool.Fire,
            IsScroll: false,
            IsReaction: false,
            IsLearnable: true,
            MinMageLevel: 1,
            Price: 10,
            Effect: "Zraň jeden cíl za 1k6 ž ohněm.");

        var resp = await Client.PostAsJsonAsync("/api/spells", dto);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        var created = await resp.Content.ReadFromJsonAsync<SpellDetailDto>();
        Assert.NotNull(created);
        Assert.Equal("Ohnivá střela", created.Name);
        Assert.Equal(1, created.Level);
        Assert.Equal(SpellSchool.Fire, created.School);
        Assert.True(created.IsLearnable);
    }

    [Fact]
    public async Task Create_DuplicateName_ReturnsConflict()
    {
        var dto = new CreateSpellDto(
            "Léčivé dlaně", 0, 0, SpellSchool.Support, true, false, false, 0, null,
            "Vyleč 5 ž ztracených v tomto kole.");

        var first = await Client.PostAsJsonAsync("/api/spells", dto);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await Client.PostAsJsonAsync("/api/spells", dto);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task GetAll_OrderedByLevelThenName()
    {
        var now = Guid.NewGuid().ToString("N")[..6];  // disambiguate name across runs if any

        await Client.PostAsJsonAsync("/api/spells",
            new CreateSpellDto($"Ohnivá bouře {now}", 5, 5, SpellSchool.Fire, false, false, true, 5, 40,
                "Zraň všechny nepřátele."));
        await Client.PostAsJsonAsync("/api/spells",
            new CreateSpellDto($"Jiskra {now}", 0, 0, SpellSchool.Fire, true, false, false, 0, null,
                "3 ž ohněm."));
        await Client.PostAsJsonAsync("/api/spells",
            new CreateSpellDto($"Omámení {now}", 1, 1, SpellSchool.Mental, false, false, true, 1, 10,
                "Cílová bytost přijde o akci."));

        var all = await Client.GetFromJsonAsync<List<SpellListDto>>("/api/spells");
        Assert.NotNull(all);

        var mine = all.Where(s => s.Name.EndsWith(now)).ToList();
        Assert.Equal(3, mine.Count);
        Assert.Equal($"Jiskra {now}", mine[0].Name);        // level 0 first
        Assert.Equal($"Omámení {now}", mine[1].Name);       // level 1
        Assert.Equal($"Ohnivá bouře {now}", mine[2].Name);  // level 5 last
    }

    [Fact]
    public async Task GameSpell_AssignAndFilterByGame_Works()
    {
        // Create a game
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Spell Test", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 2)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        // Create two spells
        var s1 = await (await Client.PostAsJsonAsync("/api/spells",
            new CreateSpellDto("Jedový šíp", 0, 0, SpellSchool.Poison, true, false, false, 0, null,
                "3 ž jedem.")))
            .Content.ReadFromJsonAsync<SpellDetailDto>();
        var s2 = await (await Client.PostAsJsonAsync("/api/spells",
            new CreateSpellDto("Bažina", 3, 3, SpellSchool.Utility, false, false, true, 3, 22,
                "Výsledky blízkých útoků = [1].")))
            .Content.ReadFromJsonAsync<SpellDetailDto>();

        // Assign only s1 to the game
        var assign = await Client.PostAsJsonAsync("/api/spells/game-spell",
            new CreateGameSpellDto(game!.Id, s1!.Id, Price: 0, IsFindable: true,
                AvailabilityNotes: "V truhle v Aradhryand"));
        Assert.Equal(HttpStatusCode.Created, assign.StatusCode);

        // by-game returns only the assigned one
        var byGame = await Client.GetFromJsonAsync<List<GameSpellDto>>($"/api/spells/by-game/{game.Id}");
        Assert.NotNull(byGame);
        Assert.Single(byGame);
        Assert.Equal(s1.Id, byGame[0].SpellId);
        Assert.Equal("Jedový šíp", byGame[0].SpellName);
        Assert.Equal(SpellSchool.Poison, byGame[0].School);
        Assert.True(byGame[0].IsFindable);

        // Update the per-game config
        var upd = await Client.PutAsJsonAsync($"/api/spells/game-spell/{game.Id}/{s1.Id}",
            new UpdateGameSpellDto(Price: 5, IsFindable: false, AvailabilityNotes: null));
        Assert.Equal(HttpStatusCode.NoContent, upd.StatusCode);

        byGame = await Client.GetFromJsonAsync<List<GameSpellDto>>($"/api/spells/by-game/{game.Id}");
        Assert.Equal(5, byGame![0].Price);
        Assert.False(byGame[0].IsFindable);

        // Delete the assignment
        var del = await Client.DeleteAsync($"/api/spells/game-spell/{game.Id}/{s1.Id}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        byGame = await Client.GetFromJsonAsync<List<GameSpellDto>>($"/api/spells/by-game/{game.Id}");
        Assert.Empty(byGame!);
    }

    [Fact]
    public async Task Search_FindsSpells_ByNameFragment()
    {
        // Seed a couple of spells with a recognisable token in the Effect so FTS matches.
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
            db.Spells.AddRange(
                new Spell
                {
                    Name = "Ohnivá střela (test)",
                    Level = 1, ManaCost = 1, School = SpellSchool.Fire,
                    IsScroll = false, IsReaction = false, IsLearnable = true,
                    MinMageLevel = 1, Price = 10,
                    Effect = "Zraň jeden cíl za 1k6 ž ohněm."
                },
                new Spell
                {
                    Name = "Ohnivá koule (test)",
                    Level = 3, ManaCost = 3, School = SpellSchool.Fire,
                    IsScroll = false, IsReaction = false, IsLearnable = true,
                    MinMageLevel = 3, Price = 22,
                    Effect = "Zraň až dva cíle za 1k6 ž ohněm."
                });
            await db.SaveChangesAsync();
        }

        var resp = await Client.GetAsync("/api/search?q=ohniv");
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<SearchResponseDto>();
        Assert.NotNull(body);

        var spells = body.Results.Where(r => r.EntityType == "Spell").ToList();
        Assert.Contains(spells, s => s.Name == "Ohnivá střela (test)");
        Assert.Contains(spells, s => s.Name == "Ohnivá koule (test)");
    }
}
