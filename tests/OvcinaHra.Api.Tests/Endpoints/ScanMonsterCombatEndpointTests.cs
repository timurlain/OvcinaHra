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
/// MonsterVictory/MonsterDefeat event posting (with server-side
/// OrganizerRoleAssignment authorization) plus the two undo endpoints
/// (last-note, last-combat).
///
/// The shared <see cref="PostgresFixture"/> client authenticates as
/// <c>test@ovcina.cz</c> / "Test Organizátor" — that email is what
/// <see cref="OrganizerRoleAssignment.PersonEmail"/> rows must match for
/// the monster-role lookup to succeed.
/// </summary>
public class ScanMonsterCombatEndpointTests(PostgresFixture postgres)
    : IntegrationTestBase(postgres), IClassFixture<PostgresFixture>
{
    private const string CallerEmail = "test@ovcina.cz";

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

    private async Task<int> CreateSlotAsync(int gameId)
    {
        // The exact StartTime / Duration we need is set directly via DbContext
        // afterwards (see SetSlotWindowAsync), because the API's
        // CreateGameTimeSlotDto pegs StartTime to a fixed game date that we
        // can't easily move relative to DateTime.UtcNow.
        var response = await Client.PostAsJsonAsync("/api/timeline/slots",
            new CreateGameTimeSlotDto(
                GameId: gameId,
                StartTime: new DateTime(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc),
                DurationHours: 1m,
                InGameYear: 1247,
                Stage: GameTimePhase.Start));
        response.EnsureSuccessStatusCode();
        var slot = await response.Content.ReadFromJsonAsync<GameTimeSlotDto>();
        return slot!.Id;
    }

    /// <summary>
    /// Mutates the slot's window directly in the database. Used so each
    /// authorization test can control whether "now" falls inside the
    /// slot's ±15 min window.
    /// </summary>
    private async Task SetSlotWindowAsync(int slotId, DateTime startUtc, TimeSpan duration)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
        var slot = await db.GameTimeSlots.FirstAsync(s => s.Id == slotId);
        slot.StartTime = startUtc;
        slot.Duration = duration;
        await db.SaveChangesAsync();
    }

    private async Task UpsertOrganizerRoleAsync(
        int gameId, int slotId, int npcId, string email)
    {
        var response = await Client.PutAsJsonAsync(
            $"/api/games/{gameId}/organizer-role-assignments/slots/{slotId}/npcs/{npcId}",
            new UpsertOrganizerRoleAssignmentDto(501, "Test Organizátor", email));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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
    /// One-call seed: game + slot in active window + monster NPC + role
    /// assignment for the test caller + scanned hero. Returns the personId,
    /// the npcId of the monster, and the gameId.
    /// </summary>
    private async Task<(int gameId, int personId, int monsterNpcId)> SeedActiveMonsterRoleAsync(
        int externalPersonId,
        DateTime? slotStartUtc = null,
        TimeSpan? slotDuration = null,
        NpcRole npcRole = NpcRole.Monster,
        string? assignedEmail = null)
    {
        var gameId = await CreateGameAsync();
        var npcId = await CreateNpcAsync("Skret hlídač u brány", npcRole);
        await AssignNpcToGameAsync(gameId, npcId);
        var slotId = await CreateSlotAsync(gameId);

        // Default: slot started 30 min ago, runs 2h — caller is mid-window.
        await SetSlotWindowAsync(
            slotId,
            slotStartUtc ?? DateTime.UtcNow.AddMinutes(-30),
            slotDuration ?? TimeSpan.FromHours(2));

        await UpsertOrganizerRoleAsync(gameId, slotId, npcId, assignedEmail ?? CallerEmail);
        await SeedHeroAsync(gameId, externalPersonId);

        return (gameId, externalPersonId, npcId);
    }

    [Fact]
    public async Task PostMonsterVictory_WithActiveMonsterRole_ReturnsCreatedAndPersistsEvent()
    {
        var (_, personId, npcId) = await SeedActiveMonsterRoleAsync(externalPersonId: 200);

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
    public async Task PostMonsterVictory_SlotAlreadyEnded_BeyondBuffer_Returns403()
    {
        // Slot ended 30 min ago — outside the +15 min trailing buffer.
        var (_, personId, npcId) = await SeedActiveMonsterRoleAsync(
            externalPersonId: 201,
            slotStartUtc: DateTime.UtcNow.AddHours(-2).AddMinutes(-30),
            slotDuration: TimeSpan.FromHours(2));

        var dto = new CreateCharacterEventDto(
            CharacterEventType.MonsterVictory,
            JsonSerializer.Serialize(new { monsterNpcId = npcId, monsterName = "Skret" }),
            Location: "Glejt");

        var response = await PostJsonWithIdempotencyAsync(
            $"/api/scan/{personId}/events", dto, $"victory-late-{personId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostMonsterVictory_SlotStartsInFuture_BeyondBuffer_Returns403()
    {
        // Slot starts in 30 min — outside the -15 min leading buffer.
        var (_, personId, npcId) = await SeedActiveMonsterRoleAsync(
            externalPersonId: 202,
            slotStartUtc: DateTime.UtcNow.AddMinutes(30),
            slotDuration: TimeSpan.FromHours(2));

        var dto = new CreateCharacterEventDto(
            CharacterEventType.MonsterVictory,
            JsonSerializer.Serialize(new { monsterNpcId = npcId, monsterName = "Skret" }),
            Location: "Glejt");

        var response = await PostJsonWithIdempotencyAsync(
            $"/api/scan/{personId}/events", dto, $"victory-early-{personId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostMonsterVictory_NpcRoleIsNotMonster_Returns403()
    {
        // Caller is assigned to this NPC in an active slot, but the NPC has
        // role Merchant — only Monster roles may record monster combat.
        var (_, personId, npcId) = await SeedActiveMonsterRoleAsync(
            externalPersonId: 203,
            npcRole: NpcRole.Merchant);

        var dto = new CreateCharacterEventDto(
            CharacterEventType.MonsterVictory,
            JsonSerializer.Serialize(new { monsterNpcId = npcId, monsterName = "Kupec" }),
            Location: "Glejt");

        var response = await PostJsonWithIdempotencyAsync(
            $"/api/scan/{personId}/events", dto, $"victory-merchant-{personId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostMonsterVictory_CallerNotAssignedToNpc_Returns403()
    {
        // Active monster role exists, but for a DIFFERENT email — the test
        // caller (test@ovcina.cz) does not own this monster.
        var (_, personId, npcId) = await SeedActiveMonsterRoleAsync(
            externalPersonId: 204,
            assignedEmail: "kdosi-jiny@ovcina.cz");

        var dto = new CreateCharacterEventDto(
            CharacterEventType.MonsterVictory,
            JsonSerializer.Serialize(new { monsterNpcId = npcId, monsterName = "Skret" }),
            Location: "Glejt");

        var response = await PostJsonWithIdempotencyAsync(
            $"/api/scan/{personId}/events", dto, $"victory-other-{personId}");

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
        var (_, personId, npcId) = await SeedActiveMonsterRoleAsync(externalPersonId: 310);

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
        var (_, personId, npcId) = await SeedActiveMonsterRoleAsync(externalPersonId: 311);

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

        // LevelUp is preserved; MonsterVictory is gone.
        Assert.Single(events);
        Assert.Equal(CharacterEventType.LevelUp, events[0].EventType);
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
    public async Task PostMonsterVictory_NoOrganizerRoleAssignmentAtAll_Returns403()
    {
        // Game + hero exist, but no monster NPC seeded and no role assignment
        // for the caller — payload references an NPC id that nobody owns.
        var gameId = await CreateGameAsync();
        await SeedHeroAsync(gameId, externalPersonId: 205);

        var dto = new CreateCharacterEventDto(
            CharacterEventType.MonsterVictory,
            JsonSerializer.Serialize(new { monsterNpcId = 99999, monsterName = "Fiktivní" }),
            Location: "Glejt");

        var response = await PostJsonWithIdempotencyAsync(
            "/api/scan/205/events", dto, "victory-no-role");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
