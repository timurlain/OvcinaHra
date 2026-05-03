using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Tests.Fixtures;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Tests.Endpoints;

/// <summary>
/// Integration tests for the Glejt monster-combat surface on /api/scan:
/// MonsterVictory/MonsterDefeat event posting (with the loosened
/// "NPC must be a Monster in this game" sanity gate, #523) plus the two
/// undo endpoints (last-note, last-combat).
///
/// The shared <see cref="PostgresFixture"/> client authenticates as
/// <c>test@ovcina.cz</c> / "Test Organizátor". Since #523 the auth path
/// no longer reads OrganizerRoleAssignment rows, so the caller's email is
/// not load-bearing for these tests — only the NPC's Role flag is.
/// </summary>
public class ScanMonsterCombatEndpointTests(PostgresFixture postgres)
    : IntegrationTestBase(postgres), IClassFixture<PostgresFixture>
{
    private async Task<HttpResponseMessage> PostJsonWithIdempotencyAsync<T>(
        string url, T payload, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Add("X-Idempotency-Key", key);
        return await Client.SendAsync(request);
    }

    private async Task<int> CreateGameAsync(string name = "Souboj s nestvůrou")
    {
        var response = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto(name, 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        response.EnsureSuccessStatusCode();
        var game = await response.Content.ReadFromJsonAsync<GameDetailDto>();
        return game!.Id;
    }

    private async Task<int> CreateNpcAsync(string name, NpcRole role)
    {
        var response = await Client.PostAsJsonAsync("/api/npcs",
            new CreateNpcDto(name, role, "Testovací nestvůra"));
        response.EnsureSuccessStatusCode();
        var npc = await response.Content.ReadFromJsonAsync<NpcDetailDto>();
        return npc!.Id;
    }

    private async Task AssignNpcToGameAsync(int gameId, int npcId)
    {
        var response = await Client.PostAsJsonAsync("/api/npcs/game-npc",
            new CreateGameNpcDto(gameId, npcId));
        response.EnsureSuccessStatusCode();
    }

    private async Task<int> SeedHeroAsync(int gameId, int externalPersonId)
    {
        var createResponse = await Client.PostAsJsonAsync("/api/characters",
            new CreateCharacterDto(
                "Frodo",
                Race: Race.Hobbit,
                IsPlayedCharacter: true,
                ExternalPersonId: externalPersonId));
        createResponse.EnsureSuccessStatusCode();
        var character = await createResponse.Content.ReadFromJsonAsync<CharacterDetailDto>();

        var assignResponse = await Client.PostAsJsonAsync(
            $"/api/characters/{character!.Id}/assignments",
            new CreateCharacterAssignmentDto(gameId, externalPersonId, null, null));
        assignResponse.EnsureSuccessStatusCode();
        return character.Id;
    }

    /// <summary>
    /// One-call seed for a game with a Monster NPC linked to it and a hero
    /// scanned in. Returns gameId, the hero's external personId, and the
    /// monster NpcId. Since the #523 loosening there is no slot or
    /// OrganizerRoleAssignment in the seed — the auth gate only checks
    /// the NPC's Role + GameNpc link.
    /// </summary>
    private async Task<(int gameId, int personId, int monsterNpcId)> SeedMonsterInGameAsync(
        int externalPersonId,
        NpcRole npcRole = NpcRole.Monster,
        string npcName = "Skret hlídač u brány")
    {
        var gameId = await CreateGameAsync();
        var npcId = await CreateNpcAsync(npcName, npcRole);
        await AssignNpcToGameAsync(gameId, npcId);
        await SeedHeroAsync(gameId, externalPersonId);
        return (gameId, externalPersonId, npcId);
    }

    [Fact]
    public async Task PostMonsterVictory_NpcIsMonsterInGame_ReturnsCreatedAndPersistsEvent()
    {
        var (_, personId, npcId) = await SeedMonsterInGameAsync(externalPersonId: 200);

        var dto = new CreateCharacterEventDto(
            CharacterEventType.MonsterVictory,
            JsonSerializer.Serialize(new { monsterNpcId = npcId, monsterName = "Skret hlídač u brány" }),
            Location: "Glejt");

        var response = await PostJsonWithIdempotencyAsync(
            $"/api/scan/{personId}/events", dto, $"victory-{personId}");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
        var saved = await db.CharacterEvents
            .Where(e => e.Assignment.ExternalPersonId == personId
                && e.EventType == CharacterEventType.MonsterVictory)
            .ToListAsync();
        Assert.Single(saved);
        Assert.Contains("\"monsterNpcId\":" + npcId, saved[0].Data);
    }

    [Fact]
    public async Task PostMonsterVictory_NpcRoleIsNotMonster_Returns403()
    {
        // The NPC exists in this game but its role is Merchant — only NPCs
        // flagged Monster may receive monster-combat events. This is the
        // single role gate that survives the #523 loosening.
        var (_, personId, npcId) = await SeedMonsterInGameAsync(
            externalPersonId: 203,
            npcRole: NpcRole.Merchant,
            npcName: "Kupec");

        var dto = new CreateCharacterEventDto(
            CharacterEventType.MonsterVictory,
            JsonSerializer.Serialize(new { monsterNpcId = npcId, monsterName = "Kupec" }),
            Location: "Glejt");

        var response = await PostJsonWithIdempotencyAsync(
            $"/api/scan/{personId}/events", dto, $"victory-merchant-{personId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostMonsterVictory_NpcInDifferentGame_Returns403()
    {
        // Monster NPC exists, and there's a hero in gameA, but the NPC is
        // linked to gameB. The auth gate must reject because the GameNpc
        // join doesn't connect them — preserves cross-game isolation.
        var gameA = await CreateGameAsync("Game A");
        await SeedHeroAsync(gameA, externalPersonId: 207);

        var gameB = await CreateGameAsync("Game B");
        var npcId = await CreateNpcAsync("Skret z jiné hry", NpcRole.Monster);
        await AssignNpcToGameAsync(gameB, npcId);

        var dto = new CreateCharacterEventDto(
            CharacterEventType.MonsterVictory,
            JsonSerializer.Serialize(new { monsterNpcId = npcId, monsterName = "Skret z jiné hry" }),
            Location: "Glejt");

        var response = await PostJsonWithIdempotencyAsync(
            "/api/scan/207/events", dto, "victory-cross-game");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeleteLastNote_WithExistingNote_Returns204AndRemovesEvent()
    {
        var gameId = await CreateGameAsync("Note undo");
        await SeedHeroAsync(gameId, externalPersonId: 300);

        // Two notes; the newest one should be removed.
        var firstNote = await Client.PostAsJsonAsync("/api/scan/300/events",
            new CreateCharacterEventDto(CharacterEventType.Note, """{"note":"první"}""", "Bran"));
        firstNote.EnsureSuccessStatusCode();
        var secondNote = await Client.PostAsJsonAsync("/api/scan/300/events",
            new CreateCharacterEventDto(CharacterEventType.Note, """{"note":"druhá"}""", "Bran"));
        secondNote.EnsureSuccessStatusCode();

        var response = await Client.DeleteAsync("/api/scan/300/events/last-note");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
        var notes = await db.CharacterEvents
            .Where(e => e.Assignment.ExternalPersonId == 300
                && e.EventType == CharacterEventType.Note)
            .OrderBy(e => e.Timestamp)
            .ToListAsync();
        Assert.Single(notes);
        // Note payload is forwarded verbatim by the API; assert against the
        // JSON-encoded form actually stored.
        Assert.Contains("prvn", notes[0].Data, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("druh", notes[0].Data, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteLastNote_NoNoteHistory_Returns404()
    {
        var gameId = await CreateGameAsync("Note 404");
        await SeedHeroAsync(gameId, externalPersonId: 301);

        var response = await Client.DeleteAsync("/api/scan/301/events/last-note");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteLastCombat_RemovesNewestMonsterEvent()
    {
        var (_, personId, npcId) = await SeedMonsterInGameAsync(externalPersonId: 310);

        // Two combat events, second one is the newest.
        var first = await PostJsonWithIdempotencyAsync(
            $"/api/scan/{personId}/events",
            new CreateCharacterEventDto(
                CharacterEventType.MonsterVictory,
                JsonSerializer.Serialize(new { monsterNpcId = npcId, monsterName = "První" }),
                Location: "Glejt"),
            $"combat-1-{personId}");
        first.EnsureSuccessStatusCode();

        var second = await PostJsonWithIdempotencyAsync(
            $"/api/scan/{personId}/events",
            new CreateCharacterEventDto(
                CharacterEventType.MonsterDefeat,
                JsonSerializer.Serialize(new { monsterNpcId = npcId, monsterName = "Druhý" }),
                Location: "Glejt"),
            $"combat-2-{personId}");
        second.EnsureSuccessStatusCode();

        var response = await Client.DeleteAsync($"/api/scan/{personId}/events/last-combat");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
        var combat = await db.CharacterEvents
            .Where(e => e.Assignment.ExternalPersonId == personId
                && (e.EventType == CharacterEventType.MonsterVictory
                    || e.EventType == CharacterEventType.MonsterDefeat))
            .ToListAsync();
        Assert.Single(combat);
        Assert.Equal(CharacterEventType.MonsterVictory, combat[0].EventType);
        // JsonSerializer escapes non-ASCII by default; match the escaped form
        // so the assertion isn't sensitive to encoder settings.
        Assert.Contains("Prvn\\u00ED", combat[0].Data, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteLastCombat_IgnoresLevelUpAfterCombat()
    {
        // The undo scope is "newest combat row", NOT "newest event of any
        // type". A LevelUp recorded AFTER a MonsterVictory must not be
        // touched by last-combat — that's last-levelup's job.
        var (_, personId, npcId) = await SeedMonsterInGameAsync(externalPersonId: 311);

        var combat = await PostJsonWithIdempotencyAsync(
            $"/api/scan/{personId}/events",
            new CreateCharacterEventDto(
                CharacterEventType.MonsterVictory,
                JsonSerializer.Serialize(new { monsterNpcId = npcId, monsterName = "Skret" }),
                Location: "Glejt"),
            $"combat-{personId}");
        combat.EnsureSuccessStatusCode();

        var levelUp = await Client.PostAsJsonAsync($"/api/scan/{personId}/events",
            new CreateCharacterEventDto(CharacterEventType.LevelUp, "{}"));
        levelUp.EnsureSuccessStatusCode();

        var response = await Client.DeleteAsync($"/api/scan/{personId}/events/last-combat");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
        var events = await db.CharacterEvents
            .Where(e => e.Assignment.ExternalPersonId == personId)
            .OrderBy(e => e.Timestamp)
            .ToListAsync();

        // LevelUp is preserved; MonsterVictory is gone. The revert audit row is
        // intentionally observable and must not count as remaining combat.
        Assert.Single(events, e => e.EventType == CharacterEventType.LevelUp);
        Assert.DoesNotContain(events, e => e.EventType == CharacterEventType.MonsterVictory);
        Assert.Single(events, e => e.EventType == CharacterEventType.MonsterCombatReverted);
    }

    [Fact]
    public async Task PostMonsterVictory_SameIdempotencyKey_Twice_PersistsOnceAndReplaysResponse()
    {
        var (_, personId, npcId) = await SeedMonsterInGameAsync(externalPersonId: 320);

        var dto = new CreateCharacterEventDto(
            CharacterEventType.MonsterVictory,
            JsonSerializer.Serialize(new { monsterNpcId = npcId, monsterName = "Skret" }),
            Location: "Glejt");
        const string key = "monster-victory-replay";

        var first = await PostJsonWithIdempotencyAsync(
            $"/api/scan/{personId}/events", dto, key);
        var second = await PostJsonWithIdempotencyAsync(
            $"/api/scan/{personId}/events", dto, key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var firstDto = await first.Content.ReadFromJsonAsync<CharacterEventDto>();
        var secondDto = await second.Content.ReadFromJsonAsync<CharacterEventDto>();
        Assert.Equal(firstDto!.Id, secondDto!.Id);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
        var combatRows = await db.CharacterEvents
            .Where(e => e.Assignment.ExternalPersonId == personId
                && e.EventType == CharacterEventType.MonsterVictory)
            .CountAsync();
        Assert.Equal(1, combatRows);
    }

    [Fact]
    public async Task DeleteLastCombat_NoCombatHistory_Returns404()
    {
        var gameId = await CreateGameAsync("Combat 404");
        await SeedHeroAsync(gameId, externalPersonId: 312);

        // Hero has a Note but no combat — must return 404.
        var note = await Client.PostAsJsonAsync("/api/scan/312/events",
            new CreateCharacterEventDto(CharacterEventType.Note, """{"note":"jen poznámka"}"""));
        note.EnsureSuccessStatusCode();

        var response = await Client.DeleteAsync("/api/scan/312/events/last-combat");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostMonsterVictory_PayloadMissingMonsterNpcId_Returns400()
    {
        // The payload omits the load-bearing monsterNpcId field.
        // AuthorizeMonsterEventAsync must refuse to assume an NPC id and
        // return 400 with a Czech problem detail naming the missing field.
        // This same branch also fires when dto.Data is malformed JSON
        // (TryReadMonsterNpcId returns null in both cases).
        var (_, personId, _) = await SeedMonsterInGameAsync(externalPersonId: 206);

        var dto = new CreateCharacterEventDto(
            CharacterEventType.MonsterVictory,
            JsonSerializer.Serialize(new { monsterName = "Skret" }),
            Location: "Glejt");

        var response = await PostJsonWithIdempotencyAsync(
            $"/api/scan/{personId}/events", dto, $"victory-no-npcid-{personId}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("monsterNpcId", body, StringComparison.Ordinal);
    }
}
