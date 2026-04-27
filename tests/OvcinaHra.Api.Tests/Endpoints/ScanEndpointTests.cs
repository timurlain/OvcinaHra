using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Services;
using OvcinaHra.Api.Tests.Fixtures;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Tests.Endpoints;

public class ScanEndpointTests(PostgresFixture postgres) : IntegrationTestBase(postgres), IClassFixture<PostgresFixture>
{
    private async Task<int> GetKingdomIdAsync(string name)
    {
        var kingdoms = await Client.GetFromJsonAsync<List<KingdomDto>>("/api/kingdoms");
        return kingdoms!.First(k => k.Name == name).Id;
    }

    private async Task<GameDetailDto> CreateGameAsync(string name)
    {
        var response = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto(name, 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GameDetailDto>())!;
    }

    private async Task<LocationDetailDto> CreateLocationAsync(string name)
    {
        var response = await Client.PostAsJsonAsync("/api/locations",
            new CreateLocationDto(name, LocationKind.Wilderness, 49.5m, 17.1m));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LocationDetailDto>())!;
    }

    private async Task<SecretStashDetailDto> CreateSecretStashAsync(string name)
    {
        var response = await Client.PostAsJsonAsync("/api/secret-stashes",
            new CreateSecretStashDto(name));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SecretStashDetailDto>())!;
    }

    private async Task<HttpResponseMessage> PostJsonWithIdempotencyAsync<T>(
        string url,
        T payload,
        string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Add("X-Idempotency-Key", key);
        return await Client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> DeleteWithIdempotencyAsync(string url, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, url);
        request.Headers.Add("X-Idempotency-Key", key);
        return await Client.SendAsync(request);
    }

    private async Task<(CharacterDetailDto character, CharacterAssignmentDto assignment)> SeedCharacterWithAssignment(
        int externalPersonId = 100, int gameId = 1,
        Race? race = Race.Dwarf, int? kingdomId = null)
    {
        var createResponse = await Client.PostAsJsonAsync("/api/characters",
            new CreateCharacterDto("Thorin", Race: race, IsPlayedCharacter: true, ExternalPersonId: externalPersonId));
        createResponse.EnsureSuccessStatusCode();
        var character = await createResponse.Content.ReadFromJsonAsync<CharacterDetailDto>();

        var assignResponse = await Client.PostAsJsonAsync(
            $"/api/characters/{character!.Id}/assignments",
            new CreateCharacterAssignmentDto(gameId, externalPersonId, null, kingdomId));
        assignResponse.EnsureSuccessStatusCode();
        var assignment = await assignResponse.Content.ReadFromJsonAsync<CharacterAssignmentDto>();

        return (character, assignment!);
    }

    [Fact]
    public async Task Scan_ActiveCharacter_ReturnsProfile()
    {
        var dwarvesKingdomId = await GetKingdomIdAsync("Azanulinbar-Dum");
        await SeedCharacterWithAssignment(externalPersonId: 100, kingdomId: dwarvesKingdomId);

        var response = await Client.GetAsync("/api/scan/100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var profile = await response.Content.ReadFromJsonAsync<ScanCharacterDto>();
        Assert.NotNull(profile);
        Assert.Equal("Thorin", profile.Name);
        Assert.Null(profile.PlayerFullName); // no player name set in seed
        Assert.Equal(Race.Dwarf, profile.Race);
        Assert.Equal("Azanulinbar-Dum", profile.Kingdom);
        Assert.Equal(0, profile.CurrentLevel);
        Assert.Equal(0, profile.TotalXp);
        Assert.Empty(profile.Skills);
        Assert.Empty(profile.RecentEvents);
    }

    [Fact]
    public async Task Scan_NoActiveAssignment_ReturnsNotFound()
    {
        var response = await Client.GetAsync("/api/scan/9999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostEvent_LevelUp_IncrementsLevel()
    {
        await SeedCharacterWithAssignment(externalPersonId: 101);

        var eventResponse = await Client.PostAsJsonAsync("/api/scan/101/events",
            new CreateCharacterEventDto(CharacterEventType.LevelUp, "{}"));
        Assert.Equal(HttpStatusCode.Created, eventResponse.StatusCode);

        var profile = await Client.GetFromJsonAsync<ScanCharacterDto>("/api/scan/101");
        Assert.NotNull(profile);
        Assert.Equal(1, profile.CurrentLevel);
        Assert.Equal(1, profile.TotalXp);
    }

    [Fact]
    public async Task PostEvent_WithSameIdempotencyKey_ReplaysOriginalEvent()
    {
        await SeedCharacterWithAssignment(externalPersonId: 111);
        var payload = new CreateCharacterEventDto(CharacterEventType.LevelUp, "{}");

        var firstResponse = await PostJsonWithIdempotencyAsync(
            "/api/scan/111/events", payload, "scan-level-111");
        var secondResponse = await PostJsonWithIdempotencyAsync(
            "/api/scan/111/events", payload, "scan-level-111");

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        var first = await firstResponse.Content.ReadFromJsonAsync<CharacterEventDto>();
        var second = await secondResponse.Content.ReadFromJsonAsync<CharacterEventDto>();
        Assert.Equal(first!.Id, second!.Id);

        var profile = await Client.GetFromJsonAsync<ScanCharacterDto>("/api/scan/111");
        Assert.Equal(1, profile!.CurrentLevel);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
        var key = await db.EventIdempotencies.SingleAsync();
        key.CreatedAtUtc = DateTime.UtcNow.AddDays(-8);
        await db.SaveChangesAsync();
        var deleted = await EventIdempotencyCleanupService.CleanupAsync(db, DateTime.UtcNow);
        Assert.Equal(1, deleted);
    }

    [Fact]
    public async Task PostEvent_ClassChosen_SetsAssignmentClass()
    {
        await SeedCharacterWithAssignment(externalPersonId: 102);

        var eventResponse = await Client.PostAsJsonAsync("/api/scan/102/events",
            new CreateCharacterEventDto(CharacterEventType.ClassChosen, """{"class":"Warrior"}"""));
        Assert.Equal(HttpStatusCode.Created, eventResponse.StatusCode);

        var profile = await Client.GetFromJsonAsync<ScanCharacterDto>("/api/scan/102");
        Assert.NotNull(profile);
        Assert.Equal(PlayerClass.Warrior, profile.Class);
    }

    [Fact]
    public async Task PostEvent_ClassChosen_AlreadySet_ReturnsBadRequest()
    {
        await SeedCharacterWithAssignment(externalPersonId: 103);

        await Client.PostAsJsonAsync("/api/scan/103/events",
            new CreateCharacterEventDto(CharacterEventType.ClassChosen, """{"class":"Warrior"}"""));

        var secondResponse = await Client.PostAsJsonAsync("/api/scan/103/events",
            new CreateCharacterEventDto(CharacterEventType.ClassChosen, """{"class":"Mage"}"""));

        Assert.Equal(HttpStatusCode.BadRequest, secondResponse.StatusCode);
    }

    [Fact]
    public async Task GetEvents_ReturnsRecentEvents()
    {
        await SeedCharacterWithAssignment(externalPersonId: 104);

        await Client.PostAsJsonAsync("/api/scan/104/events",
            new CreateCharacterEventDto(CharacterEventType.LevelUp, "{}"));
        await Client.PostAsJsonAsync("/api/scan/104/events",
            new CreateCharacterEventDto(CharacterEventType.Note, """{"note":"Found a sword"}""", "Rivendell"));

        var response = await Client.GetAsync("/api/scan/104/events");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var events = await response.Content.ReadFromJsonAsync<List<CharacterEventDto>>();
        Assert.NotNull(events);
        Assert.Equal(2, events.Count);
        // Most recent first
        Assert.Equal(CharacterEventType.Note, events[0].EventType);
        Assert.Equal(CharacterEventType.LevelUp, events[1].EventType);
    }

    [Fact]
    public async Task DeleteLastLevelUp_RemovesMostRecentLevelUp_AndWritesAudit()
    {
        await SeedCharacterWithAssignment(externalPersonId: 105);

        await Client.PostAsJsonAsync("/api/scan/105/events",
            new CreateCharacterEventDto(CharacterEventType.LevelUp, "{}"));
        await Client.PostAsJsonAsync("/api/scan/105/events",
            new CreateCharacterEventDto(CharacterEventType.LevelUp, "{}"));

        var response = await Client.DeleteAsync("/api/scan/105/events/last-levelup");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var audit = await response.Content.ReadFromJsonAsync<CharacterEventDto>();
        Assert.NotNull(audit);
        Assert.Equal(CharacterEventType.LevelUpReverted, audit.EventType);
        Assert.Equal("Test Organizátor", audit.OrganizerName);
        Assert.Contains("\"revertedLevel\":2", audit.Data);

        var profile = await Client.GetFromJsonAsync<ScanCharacterDto>("/api/scan/105");
        Assert.NotNull(profile);
        Assert.Equal(1, profile.CurrentLevel);
        Assert.Equal(1, profile.TotalXp);

        var events = await Client.GetFromJsonAsync<List<CharacterEventDto>>("/api/scan/105/events");
        Assert.NotNull(events);
        Assert.Equal(1, events.Count(e => e.EventType == CharacterEventType.LevelUp));
        Assert.Contains(events, e => e.EventType == CharacterEventType.LevelUpReverted);
    }

    [Fact]
    public async Task DeleteLastLevelUp_WhenNoLevelUp_ReturnsNotFound()
    {
        await SeedCharacterWithAssignment(externalPersonId: 106);

        var response = await Client.DeleteAsync("/api/scan/106/events/last-levelup");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteLastLevelUp_WithSameIdempotencyKey_ReplaysAuditEvent()
    {
        await SeedCharacterWithAssignment(externalPersonId: 112);
        await Client.PostAsJsonAsync("/api/scan/112/events",
            new CreateCharacterEventDto(CharacterEventType.LevelUp, "{}"));

        var firstResponse = await DeleteWithIdempotencyAsync(
            "/api/scan/112/events/last-levelup", "undo-level-112");
        var secondResponse = await DeleteWithIdempotencyAsync(
            "/api/scan/112/events/last-levelup", "undo-level-112");

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        var first = await firstResponse.Content.ReadFromJsonAsync<CharacterEventDto>();
        var second = await secondResponse.Content.ReadFromJsonAsync<CharacterEventDto>();
        Assert.Equal(first!.Id, second!.Id);

        var events = await Client.GetFromJsonAsync<List<CharacterEventDto>>("/api/scan/112/events");
        Assert.NotNull(events);
        Assert.DoesNotContain(events, e => e.EventType == CharacterEventType.LevelUp);
        Assert.Single(events, e => e.EventType == CharacterEventType.LevelUpReverted);
    }

    [Fact]
    public async Task PendingTreasureQuests_ReturnsUnverifiedStashQuest()
    {
        var game = await CreateGameAsync("Scan poklad");
        var location = await CreateLocationAsync("Stará studna");
        var stash = await CreateSecretStashAsync("Dutý pařez");
        await Client.PostAsJsonAsync("/api/secret-stashes/game-stash",
            new CreateGameSecretStashDto(game.Id, stash.Id, location.Id));
        await SeedCharacterWithAssignment(externalPersonId: 107, gameId: game.Id);

        var createQuest = await Client.PostAsJsonAsync("/api/treasure-quests",
            new CreateTreasureQuestDto("Razítko ve skrýši", GameTimePhase.Early, game.Id, SecretStashId: stash.Id));
        var quest = await createQuest.Content.ReadFromJsonAsync<TreasureQuestListDto>();

        var pending = await Client.GetFromJsonAsync<List<PendingTreasureQuestDto>>(
            "/api/scan/107/treasure-quests/pending");

        Assert.NotNull(pending);
        var row = Assert.Single(pending);
        Assert.Equal(quest!.Id, row.QuestId);
        Assert.Equal(stash.Id, row.ExpectedStashId);
        Assert.Equal(stash.Name, row.ExpectedStashName);
        Assert.Equal(location.Id, row.ExpectedLocationId);
        Assert.Equal(location.Name, row.ExpectedLocationName);
    }

    [Fact]
    public async Task VerifyTreasureQuest_CreatesEventCreditsRewards_AndRemovesFromPending()
    {
        var game = await CreateGameAsync("Scan ověření");
        var location = await CreateLocationAsync("Dračí kámen");
        var stash = await CreateSecretStashAsync("Pod kamenem");
        await Client.PostAsJsonAsync("/api/secret-stashes/game-stash",
            new CreateGameSecretStashDto(game.Id, stash.Id, location.Id));
        await SeedCharacterWithAssignment(externalPersonId: 108, gameId: game.Id);

        var createQuest = await Client.PostAsJsonAsync("/api/treasure-quests",
            new CreateTreasureQuestDto("Dračí razítko", GameTimePhase.Midgame, game.Id, SecretStashId: stash.Id));
        var quest = await createQuest.Content.ReadFromJsonAsync<TreasureQuestListDto>();
        var itemResponse = await Client.PostAsJsonAsync("/api/items", new CreateItemDto("Dračí šupina", ItemType.Weapon));
        var item = await itemResponse.Content.ReadFromJsonAsync<ItemDetailDto>();
        await Client.PostAsJsonAsync($"/api/treasure-quests/{quest!.Id}/items",
            new AddTreasureItemDto(item!.Id, 2));

        var response = await Client.PostAsJsonAsync(
            $"/api/scan/108/treasure-quests/{quest.Id}/verify",
            new VerifyTreasureQuestDto(stash.Id, MatchConfidence: 0.98));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var ev = await response.Content.ReadFromJsonAsync<CharacterEventDto>();
        Assert.NotNull(ev);
        Assert.Equal(CharacterEventType.TreasureQuestStampVerified, ev.EventType);
        Assert.Equal("Test Organizátor", ev.OrganizerName);
        using var doc = JsonDocument.Parse(ev.Data);
        var reward = doc.RootElement.GetProperty("rewards")[0];
        Assert.Equal("Dračí šupina", reward.GetProperty("itemName").GetString());
        Assert.Equal(2, reward.GetProperty("count").GetInt32());

        var pending = await Client.GetFromJsonAsync<List<PendingTreasureQuestDto>>(
            "/api/scan/108/treasure-quests/pending");
        Assert.NotNull(pending);
        Assert.Empty(pending);
    }

    [Fact]
    public async Task VerifyTreasureQuest_WithSameIdempotencyKey_ReplaysOriginalEvent()
    {
        var game = await CreateGameAsync("Scan idempotence");
        var stash = await CreateSecretStashAsync("Idempotentní skrýš");
        await SeedCharacterWithAssignment(externalPersonId: 113, gameId: game.Id);
        var createQuest = await Client.PostAsJsonAsync("/api/treasure-quests",
            new CreateTreasureQuestDto("Jedno razítko", GameTimePhase.Early, game.Id, SecretStashId: stash.Id));
        var quest = await createQuest.Content.ReadFromJsonAsync<TreasureQuestListDto>();
        var payload = new VerifyTreasureQuestDto(stash.Id);

        var firstResponse = await PostJsonWithIdempotencyAsync(
            $"/api/scan/113/treasure-quests/{quest!.Id}/verify", payload, "verify-113");
        var secondResponse = await PostJsonWithIdempotencyAsync(
            $"/api/scan/113/treasure-quests/{quest.Id}/verify", payload, "verify-113");

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        var first = await firstResponse.Content.ReadFromJsonAsync<CharacterEventDto>();
        var second = await secondResponse.Content.ReadFromJsonAsync<CharacterEventDto>();
        Assert.Equal(first!.Id, second!.Id);
    }

    [Fact]
    public async Task VerifyTreasureQuest_OverrideWithoutReason_ReturnsProblemDetails()
    {
        var game = await CreateGameAsync("Scan override");
        var stash = await CreateSecretStashAsync("Správná skrýš");
        await SeedCharacterWithAssignment(externalPersonId: 109, gameId: game.Id);
        var createQuest = await Client.PostAsJsonAsync("/api/treasure-quests",
            new CreateTreasureQuestDto("Přepis razítka", GameTimePhase.Early, game.Id, SecretStashId: stash.Id));
        var quest = await createQuest.Content.ReadFromJsonAsync<TreasureQuestListDto>();

        var response = await Client.PostAsJsonAsync(
            $"/api/scan/109/treasure-quests/{quest!.Id}/verify",
            new VerifyTreasureQuestDto(stash.Id, Override: true));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("Uložení selhalo", problem.Title);
        Assert.Equal("Při ručním přepsání musíš uvést důvod.", problem.Detail);
    }

    private sealed record ProblemDetails(string? Title, string? Detail, int? Status);
}
