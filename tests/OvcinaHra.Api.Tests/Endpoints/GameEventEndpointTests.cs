using System.Net;
using System.Net.Http.Json;
using OvcinaHra.Api.Tests.Fixtures;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Tests.Endpoints;

public class GameEventEndpointTests(PostgresFixture postgres) : IntegrationTestBase(postgres)
{
    // ─── Helpers ──────────────────────────────────────────────────────────────

    private async Task<GameDetailDto> CreateGame()
    {
        var resp = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        return (await resp.Content.ReadFromJsonAsync<GameDetailDto>())!;
    }

    private async Task<GameTimeSlotDto> CreateSlot(int gameId, DateTime startTime, decimal durationHours = 2m)
    {
        var resp = await Client.PostAsJsonAsync("/api/timeline/slots",
            new CreateGameTimeSlotDto(gameId, startTime, durationHours));
        return (await resp.Content.ReadFromJsonAsync<GameTimeSlotDto>())!;
    }

    private async Task<NpcDetailDto> CreateNpc(string name = "Gandalf")
    {
        var resp = await Client.PostAsJsonAsync("/api/npcs",
            new CreateNpcDto(name, NpcRole.Story, "Testový NPC"));
        return (await resp.Content.ReadFromJsonAsync<NpcDetailDto>())!;
    }

    private async Task<GameEventDetailDto> CreateEvent(int gameId, int slotId, string name = "Bitva u brány")
    {
        var resp = await Client.PostAsJsonAsync($"/api/games/{gameId}/events",
            new CreateGameEventDto(name, null, [slotId], [], [], []));
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<GameEventDetailDto>())!;
    }

    // ─── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListByGame_Empty_ReturnsEmpty()
    {
        var game = await CreateGame();

        var list = await Client.GetFromJsonAsync<List<GameEventListDto>>($"/api/games/{game.Id}/events");

        Assert.NotNull(list);
        Assert.Empty(list);
    }

    [Fact]
    public async Task Create_NoTimeSlots_Returns400()
    {
        var game = await CreateGame();

        var resp = await Client.PostAsJsonAsync($"/api/games/{game.Id}/events",
            new CreateGameEventDto("Událost bez slotu", null, [], [], [], []));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Create_WithTimeSlot_Returns201()
    {
        var game = await CreateGame();
        var slot = await CreateSlot(game.Id, new DateTime(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc));

        var resp = await Client.PostAsJsonAsync($"/api/games/{game.Id}/events",
            new CreateGameEventDto("Bitva u brány", "Velká bitva", [slot.Id], [], [], []));

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var created = await resp.Content.ReadFromJsonAsync<GameEventDetailDto>();
        Assert.NotNull(created);
        Assert.Equal("Bitva u brány", created.Name);
        Assert.Equal("Velká bitva", created.Description);
        Assert.Single(created.TimeSlots);
        Assert.Equal(slot.Id, created.TimeSlots[0].TimeSlotId);
    }

    [Fact]
    public async Task Create_TimeSlotFromWrongGame_Returns400()
    {
        var game1 = await CreateGame();
        var game2 = await CreateGame();
        var slot = await CreateSlot(game2.Id, new DateTime(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc));

        var resp = await Client.PostAsJsonAsync($"/api/games/{game1.Id}/events",
            new CreateGameEventDto("Událost", null, [slot.Id], [], [], []));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task GetById_ExistingEvent_ReturnsDetail()
    {
        var game = await CreateGame();
        var slot = await CreateSlot(game.Id, new DateTime(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc));
        var created = await CreateEvent(game.Id, slot.Id);

        var resp = await Client.GetAsync($"/api/events/{created.Id}");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var detail = await resp.Content.ReadFromJsonAsync<GameEventDetailDto>();
        Assert.NotNull(detail);
        Assert.Equal(created.Id, detail.Id);
        Assert.Equal("Bitva u brány", detail.Name);
        Assert.Single(detail.TimeSlots);
    }

    [Fact]
    public async Task Update_ReplacesJunctions_ReturnsOk()
    {
        var game = await CreateGame();
        var slotA = await CreateSlot(game.Id, new DateTime(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc));
        var slotB = await CreateSlot(game.Id, new DateTime(2026, 5, 1, 14, 0, 0, DateTimeKind.Utc));
        var created = await CreateEvent(game.Id, slotA.Id);

        var resp = await Client.PutAsJsonAsync($"/api/events/{created.Id}",
            new UpdateGameEventDto("Přejmenovaná bitva", "Nový popis", [slotB.Id], [], [], []));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var updated = await resp.Content.ReadFromJsonAsync<GameEventDetailDto>();
        Assert.NotNull(updated);
        Assert.Equal("Přejmenovaná bitva", updated.Name);
        Assert.Single(updated.TimeSlots);
        Assert.Equal(slotB.Id, updated.TimeSlots[0].TimeSlotId);
    }

    [Fact]
    public async Task Delete_SoftDeletes_ReturnsNoContent()
    {
        var game = await CreateGame();
        var slot = await CreateSlot(game.Id, new DateTime(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc));
        var created = await CreateEvent(game.Id, slot.Id);

        var deleteResp = await Client.DeleteAsync($"/api/events/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);

        var getResp = await Client.GetAsync($"/api/events/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResp.StatusCode);
    }

    [Fact]
    public async Task GetCurrent_NoEventsNow_ReturnsEmpty()
    {
        var game = await CreateGame();
        // Slot far in the future
        var slot = await CreateSlot(game.Id,
            new DateTime(2099, 1, 1, 10, 0, 0, DateTimeKind.Utc));
        await CreateEvent(game.Id, slot.Id);

        var list = await Client.GetFromJsonAsync<List<GameEventDetailDto>>(
            $"/api/games/{game.Id}/events/current");

        Assert.NotNull(list);
        Assert.Empty(list);
    }

    [Fact]
    public async Task GetNext_OrdersByStartTime()
    {
        var game = await CreateGame();
        var slotLater = await CreateSlot(game.Id,
            new DateTime(2099, 6, 1, 10, 0, 0, DateTimeKind.Utc));
        var slotEarlier = await CreateSlot(game.Id,
            new DateTime(2099, 5, 1, 10, 0, 0, DateTimeKind.Utc));

        await CreateEvent(game.Id, slotLater.Id, "Pozdější");
        await CreateEvent(game.Id, slotEarlier.Id, "Dřívější");

        var list = await Client.GetFromJsonAsync<List<GameEventDetailDto>>(
            $"/api/games/{game.Id}/events/next?count=5");

        Assert.NotNull(list);
        Assert.Equal(2, list.Count);
        Assert.Equal("Dřívější", list[0].Name);
        Assert.Equal("Pozdější", list[1].Name);
    }

    [Fact]
    public async Task GetUserSchedule_NoNpcs_ReturnsEmpty()
    {
        var game = await CreateGame();

        var schedule = await Client.GetFromJsonAsync<UserScheduleDto>(
            $"/api/users/999/schedule?gameId={game.Id}");

        Assert.NotNull(schedule);
        Assert.Empty(schedule.Events);
    }

    [Fact]
    public async Task GetUserSchedule_WithNpcInEvent_ReturnsEvent()
    {
        var game = await CreateGame();
        var slot = await CreateSlot(game.Id, new DateTime(2099, 5, 1, 10, 0, 0, DateTimeKind.Utc));
        var npc = await CreateNpc("Merlin");

        // Assign NPC to game with a personId
        const int personId = 42;
        var assignResp = await Client.PostAsJsonAsync("/api/npcs/game-npc",
            new CreateGameNpcDto(game.Id, npc.Id, personId, "Hráč Merlin", "merlin@test.cz"));
        assignResp.EnsureSuccessStatusCode();

        // Create event with that NPC
        var eventResp = await Client.PostAsJsonAsync($"/api/games/{game.Id}/events",
            new CreateGameEventDto("Souboj", null, [slot.Id], [], [],
                [new CreateGameEventNpcDto(npc.Id, "útočník")]));
        eventResp.EnsureSuccessStatusCode();

        var schedule = await Client.GetFromJsonAsync<UserScheduleDto>(
            $"/api/users/{personId}/schedule?gameId={game.Id}");

        Assert.NotNull(schedule);
        Assert.Equal(personId, schedule.PersonId);
        Assert.Single(schedule.Events);
        var ev = schedule.Events[0];
        Assert.Equal("Souboj", ev.EventName);
        Assert.Equal("útočník", ev.NpcRoleInEvent);
        Assert.Single(ev.TimeSlots);
    }
}
