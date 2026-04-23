using System.Net;
using System.Net.Http.Json;
using OvcinaHra.Api.Tests.Fixtures;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Tests.Endpoints;

public class ScanEndpointTests(PostgresFixture postgres) : IntegrationTestBase(postgres)
{
    private async Task<int> GetKingdomIdAsync(string name)
    {
        var kingdoms = await Client.GetFromJsonAsync<List<KingdomDto>>("/api/kingdoms");
        return kingdoms!.First(k => k.Name == name).Id;
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
}
