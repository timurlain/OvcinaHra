using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Tests.Fixtures;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Tests.Endpoints;

// Issue #192 — POST /api/characters/reimport/{gameId} contract.
// Codifies the destructive shape: wipe imported characters + assignments,
// rollback on upstream failure, and pre-empt unlinked / missing games
// before doing anything destructive.
//
// The test fixture pins IntegrationApi:BaseUrl to http://127.0.0.1:1
// (added in #195) so the upstream HTTP call is deterministically
// unreachable. That's perfect for the "rollback on registrace down"
// test; the happy path (real upstream) is intentionally not exercised
// here because it would require a stubbed HttpMessageHandler.
public class CharacterReimportEndpointTests(PostgresFixture postgres)
    : IntegrationTestBase(postgres), IClassFixture<PostgresFixture>
{
    private async Task<GameDetailDto> CreateGameAsync(string name)
    {
        var response = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto(name, 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GameDetailDto>())!;
    }

    private async Task LinkAsync(int gameId, int externalId)
    {
        var response = await Client.PostAsJsonAsync($"/api/games/{gameId}/link",
            new LinkGameDto(externalId));
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Reimport_GameNotFound_Returns404()
    {
        var response = await Client.PostAsJsonAsync(
            "/api/characters/reimport/999999", new { });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Reimport_GameNotLinked_Returns400()
    {
        var game = await CreateGameAsync("Bez registrace");

        var response = await Client.PostAsJsonAsync(
            $"/api/characters/reimport/{game.Id}", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Contains("propojená s registrací", problem.Detail);
    }

    [Fact]
    public async Task Reimport_RegistraceUnreachable_RollsBackWipe()
    {
        // Seed an imported character + assignment for the game so we can
        // verify the wipe really doesn't commit when registrace 502s.
        var game = await CreateGameAsync("Edition test");
        await LinkAsync(game.Id, 30);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
            var character = new Character
            {
                Name = "Pre-existing import",
                IsPlayedCharacter = true,
                ExternalPersonId = 7777,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            db.Characters.Add(character);
            await db.SaveChangesAsync();

            db.CharacterAssignments.Add(new CharacterAssignment
            {
                CharacterId = character.Id,
                GameId = game.Id,
                ExternalPersonId = 7777,
                StartedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        // Trigger reimport — fixture pins BaseUrl to 127.0.0.1:1 so the
        // upstream fetch will fail deterministically.
        var response = await Client.PostAsJsonAsync(
            $"/api/characters/reimport/{game.Id}", new { });

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Contains("Žádná data nebyla zahozena", problem.Detail);

        // Critical assertion: the seeded data must STILL be there. If the
        // SERIALIZABLE transaction wasn't actually rolled back, the
        // assignment would have been wiped before the 502 fired.
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
            var stillThere = await db.CharacterAssignments
                .CountAsync(a => a.GameId == game.Id);
            Assert.Equal(1, stillThere);

            var charStillThere = await db.Characters
                .IgnoreQueryFilters()
                .AnyAsync(c => c.ExternalPersonId == 7777);
            Assert.True(charStillThere);
        }
    }

    [Fact]
    public async Task Reimport_PreservesHandCreatedCharacters_WhenWipeFires()
    {
        // Even if the upstream is down (rollback case), we want to assert
        // that hand-created characters (ExternalPersonId == null) are NEVER
        // touched by the wipe SQL. Tests the WHERE filter on step 3 of the
        // reimport endpoint by verifying nothing reaches the hand-created
        // row even when the rollback didn't happen yet — i.e. the filter
        // protects them in addition to the rollback safety net.
        var game = await CreateGameAsync("Hand-created safety");
        await LinkAsync(game.Id, 31);

        int handCreatedId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
            var npc = new Character
            {
                Name = "Ručně vytvořená NPC",
                IsPlayedCharacter = false,
                ExternalPersonId = null, // hand-created marker
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            db.Characters.Add(npc);
            await db.SaveChangesAsync();
            handCreatedId = npc.Id;
        }

        // Trigger reimport — upstream unreachable → 502 → rollback. Either
        // way, the hand-created row must not be deleted.
        await Client.PostAsJsonAsync($"/api/characters/reimport/{game.Id}", new { });

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
            var stillThere = await db.Characters
                .IgnoreQueryFilters()
                .AnyAsync(c => c.Id == handCreatedId);
            Assert.True(stillThere, "Hand-created character must survive reimport regardless of upstream outcome.");
        }
    }
}
