using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Tests.Fixtures;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Domain.ValueObjects;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Tests.Endpoints;

public class SpellEndpointTests(PostgresFixture postgres) : IntegrationTestBase(postgres), IClassFixture<PostgresFixture>
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
        gameResponse.EnsureSuccessStatusCode();
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        // Create two spells
        var s1Response = await Client.PostAsJsonAsync("/api/spells",
            new CreateSpellDto("Jedový šíp", 0, 0, SpellSchool.Poison, true, false, false, 0, null,
                "3 ž jedem."));
        s1Response.EnsureSuccessStatusCode();
        var s1 = await s1Response.Content.ReadFromJsonAsync<SpellDetailDto>();

        var s2Response = await Client.PostAsJsonAsync("/api/spells",
            new CreateSpellDto("Bažina", 3, 3, SpellSchool.Utility, false, false, true, 3, 22,
                "Výsledky blízkých útoků = [1]."));
        s2Response.EnsureSuccessStatusCode();
        var s2 = await s2Response.Content.ReadFromJsonAsync<SpellDetailDto>();

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

    // ── Issue #181 — Spell.ScrollItemId FK round-trip + cascade + validation ──

    [Fact]
    public async Task ScrollItemId_LinkViaUpdate_RoundTripsThroughGet()
    {
        var token = Guid.NewGuid().ToString("N")[..6];

        var spellResp = await Client.PostAsJsonAsync("/api/spells",
            new CreateSpellDto($"Svitek hojení {token}", 0, 0, SpellSchool.Support,
                IsScroll: true, IsReaction: false, IsLearnable: false, MinMageLevel: 0,
                Price: null, Effect: "Vyleč 5 ž v kole použití."));
        spellResp.EnsureSuccessStatusCode();
        var spell = await spellResp.Content.ReadFromJsonAsync<SpellDetailDto>();
        Assert.NotNull(spell);
        Assert.Null(spell.ScrollItemId);
        Assert.Null(spell.ScrollItemName);

        // Items don't have a public catalog POST in scope here (depends on
        // ItemEndpoints surface); seed via DbContext to keep this test focused
        // on the spell side.
        int itemId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
            var item = new Item
            {
                Name = $"Svitek hojení {token}",
                ItemType = ItemType.Scroll,
                ClassRequirements = new ClassRequirements(0, 0, 0, 0)
            };
            db.Items.Add(item);
            await db.SaveChangesAsync();
            itemId = item.Id;
        }

        var put = await Client.PutAsJsonAsync($"/api/spells/{spell.Id}",
            new UpdateSpellDto(spell.Name, 0, 0, SpellSchool.Support,
                true, false, false, 0, null, spell.Effect, null,
                ScrollItemId: itemId));
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);

        var get = await Client.GetFromJsonAsync<SpellDetailDto>($"/api/spells/{spell.Id}");
        Assert.NotNull(get);
        Assert.Equal(itemId, get.ScrollItemId);
        Assert.Equal($"Svitek hojení {token}", get.ScrollItemName);
    }

    [Fact]
    public async Task ScrollItemId_DeletingItem_NullsTheFk()
    {
        var token = Guid.NewGuid().ToString("N")[..6];

        var spellResp = await Client.PostAsJsonAsync("/api/spells",
            new CreateSpellDto($"Svitek mlhy {token}", 0, 0, SpellSchool.Frost,
                true, false, false, 0, null, "Mlha skryje cíl."));
        spellResp.EnsureSuccessStatusCode();
        var spell = await spellResp.Content.ReadFromJsonAsync<SpellDetailDto>();
        Assert.NotNull(spell);

        int itemId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
            var item = new Item
            {
                Name = $"Svitek mlhy {token}",
                ItemType = ItemType.Scroll,
                ClassRequirements = new ClassRequirements(0, 0, 0, 0)
            };
            db.Items.Add(item);
            await db.SaveChangesAsync();
            itemId = item.Id;
        }

        await Client.PutAsJsonAsync($"/api/spells/{spell.Id}",
            new UpdateSpellDto(spell.Name, 0, 0, SpellSchool.Frost,
                true, false, false, 0, null, spell.Effect, null,
                ScrollItemId: itemId));

        // Delete the Item directly via DbContext to exercise the EF cascade
        // rule (DeleteBehavior.SetNull). Going through /api/items/{id} would
        // also work but couples this test to the Item endpoint's auth flow.
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
            var item = await db.Items.FindAsync(itemId);
            db.Items.Remove(item!);
            await db.SaveChangesAsync();
        }

        var get = await Client.GetFromJsonAsync<SpellDetailDto>($"/api/spells/{spell.Id}");
        Assert.NotNull(get);
        Assert.Null(get.ScrollItemId);
        Assert.Null(get.ScrollItemName);
    }

    [Fact]
    public async Task ScrollItemId_BogusId_ReturnsBadRequestProblemDetails()
    {
        var spellResp = await Client.PostAsJsonAsync("/api/spells",
            new CreateSpellDto($"Svitek omámení {Guid.NewGuid():N}".Substring(0, 30),
                0, 0, SpellSchool.Mental, true, false, false, 0, null,
                "Cíl ztratí akci."));
        spellResp.EnsureSuccessStatusCode();
        var spell = await spellResp.Content.ReadFromJsonAsync<SpellDetailDto>();
        Assert.NotNull(spell);

        var put = await Client.PutAsJsonAsync($"/api/spells/{spell.Id}",
            new UpdateSpellDto(spell.Name, 0, 0, SpellSchool.Mental,
                true, false, false, 0, null, spell.Effect, null,
                ScrollItemId: 999_999));
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);

        var problem = await put.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("Neplatný předmět", problem.Title);
        Assert.Contains("999999", problem.Detail);
    }

    [Fact]
    public async Task ScrollItemId_BackfillSql_LinksMatchingScrollNames()
    {
        // The migration's backfill SQL ran when the fixture span up — so for
        // a deterministic test we re-execute it here against fresh seed data.
        // The query is idempotent (WHERE ScrollItemId IS NULL clause), so a
        // second run is safe. This validates the SQL is structurally correct
        // and matches case-insensitively as the previous fuzz-match did.
        var token = Guid.NewGuid().ToString("N")[..6];
        int spellId, itemId;

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();

            var item = new Item
            {
                // Mixed case on purpose — the LOWER() match should find it.
                Name = $"SVITEK PROMĚNY {token}",
                ItemType = ItemType.Scroll,
                ClassRequirements = new ClassRequirements(0, 0, 0, 0)
            };
            db.Items.Add(item);

            var spell = new Spell
            {
                Name = $"Svitek proměny {token}",
                Level = 0, ManaCost = 0, School = SpellSchool.Utility,
                IsScroll = true, IsReaction = false, IsLearnable = false,
                MinMageLevel = 0, Effect = "Krátká transformace."
            };
            db.Spells.Add(spell);
            await db.SaveChangesAsync();

            spellId = spell.Id;
            itemId = item.Id;

            // Re-run the backfill SQL verbatim from the migration. PG quoting
            // matches the migration file character-for-character.
            await db.Database.ExecuteSqlRawAsync(@"
                UPDATE ""Spells"" s
                SET ""ScrollItemId"" = i.""Id""
                FROM ""Items"" i
                WHERE s.""IsScroll"" = true
                  AND LOWER(i.""Name"") = LOWER(s.""Name"")
                  AND s.""ScrollItemId"" IS NULL;
            ");
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
            var linked = await db.Spells.FindAsync(spellId);
            Assert.NotNull(linked);
            Assert.Equal(itemId, linked.ScrollItemId);
        }
    }
}

// Minimal ProblemDetails shape for assertion — System.Text.Json deserialises
// the standard ASP.NET ProblemDetails payload into these fields.
internal sealed record ProblemDetails(string? Title, string? Detail, int? Status);
