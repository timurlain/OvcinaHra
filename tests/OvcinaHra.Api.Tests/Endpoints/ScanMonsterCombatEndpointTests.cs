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
}
