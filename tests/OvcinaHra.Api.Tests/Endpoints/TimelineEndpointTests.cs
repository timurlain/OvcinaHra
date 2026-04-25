using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Tests.Fixtures;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Tests.Endpoints;

public class TimelineEndpointTests(PostgresFixture postgres) : IntegrationTestBase(postgres), IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task GetSlotsByGame_Empty_ReturnsEmptyList()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var slots = await Client.GetFromJsonAsync<List<GameTimeSlotDto>>($"/api/timeline/slots/by-game/{game!.Id}");

        Assert.NotNull(slots);
        Assert.Empty(slots);
    }

    [Fact]
    public async Task CreateSlot_ValidSlot_ReturnsCreated()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var dto = new CreateGameTimeSlotDto(
            GameId: game!.Id,
            StartTime: new DateTime(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc),
            DurationHours: 2,
            InGameYear: 1247);

        var response = await Client.PostAsJsonAsync("/api/timeline/slots", dto);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<GameTimeSlotDto>();
        Assert.NotNull(created);
        Assert.Equal(game.Id, created.GameId);
        Assert.Equal(new DateTime(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc), created.StartTime);
        Assert.Equal(2, created.DurationHours);
        Assert.Equal(1247, created.InGameYear);
    }

    [Fact]
    public async Task UpdateSlot_ReturnsNoContent()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var createResponse = await Client.PostAsJsonAsync("/api/timeline/slots",
            new CreateGameTimeSlotDto(game!.Id, new DateTime(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc), 2));
        var slot = await createResponse.Content.ReadFromJsonAsync<GameTimeSlotDto>();

        var updateDto = new UpdateGameTimeSlotDto(
            StartTime: new DateTime(2026, 5, 1, 14, 0, 0, DateTimeKind.Utc),
            DurationHours: 3,
            InGameYear: 1248,
            Rules: "Updated rules",
            BattlefieldBonusId: null);

        var response = await Client.PutAsJsonAsync($"/api/timeline/slots/{slot!.Id}", updateDto);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task DeleteSlot_ReturnsNoContent()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var createResponse = await Client.PostAsJsonAsync("/api/timeline/slots",
            new CreateGameTimeSlotDto(game!.Id, new DateTime(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc), 2));
        var slot = await createResponse.Content.ReadFromJsonAsync<GameTimeSlotDto>();

        var response = await Client.DeleteAsync($"/api/timeline/slots/{slot!.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task GetBonusesByGame_Empty_ReturnsEmptyList()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var bonuses = await Client.GetFromJsonAsync<List<BattlefieldBonusDto>>($"/api/timeline/bonuses/by-game/{game!.Id}");

        Assert.NotNull(bonuses);
        Assert.Empty(bonuses);
    }

    [Fact]
    public async Task CreateBonus_ValidBonus_ReturnsCreated()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var dto = new CreateBattlefieldBonusDto(
            GameId: game!.Id,
            AttackBonus: 2,
            DefenseBonus: 1,
            Name: "Lesní výhoda");

        var response = await Client.PostAsJsonAsync("/api/timeline/bonuses", dto);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<BattlefieldBonusDto>();
        Assert.NotNull(created);
        Assert.Equal(game.Id, created.GameId);
        Assert.Equal("Lesní výhoda", created.Name);
        Assert.Equal(2, created.AttackBonus);
        Assert.Equal(1, created.DefenseBonus);
    }

    [Fact]
    public async Task UpdateBonus_ReturnsNoContent()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var createResponse = await Client.PostAsJsonAsync("/api/timeline/bonuses",
            new CreateBattlefieldBonusDto(game!.Id, AttackBonus: 1, DefenseBonus: 1, Name: "Původní"));
        var bonus = await createResponse.Content.ReadFromJsonAsync<BattlefieldBonusDto>();

        var updateDto = new UpdateBattlefieldBonusDto(
            Name: "Upravená výhoda",
            AttackBonus: 3,
            DefenseBonus: 2,
            Description: "Silnější bonus");

        var response = await Client.PutAsJsonAsync($"/api/timeline/bonuses/{bonus!.Id}", updateDto);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task DeleteBonus_ReturnsNoContent()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var createResponse = await Client.PostAsJsonAsync("/api/timeline/bonuses",
            new CreateBattlefieldBonusDto(game!.Id, AttackBonus: 1, DefenseBonus: 0, Name: "Ke smazání"));
        var bonus = await createResponse.Content.ReadFromJsonAsync<BattlefieldBonusDto>();

        var response = await Client.DeleteAsync($"/api/timeline/bonuses/{bonus!.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task CreateSlot_WithBattlefieldBonus_LinkIsPreserved()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var bonusResponse = await Client.PostAsJsonAsync("/api/timeline/bonuses",
            new CreateBattlefieldBonusDto(game!.Id, AttackBonus: 2, DefenseBonus: 1, Name: "Lesní výhoda"));
        var bonus = await bonusResponse.Content.ReadFromJsonAsync<BattlefieldBonusDto>();

        var slotResponse = await Client.PostAsJsonAsync("/api/timeline/slots",
            new CreateGameTimeSlotDto(
                GameId: game.Id,
                StartTime: new DateTime(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc),
                DurationHours: 2,
                InGameYear: 1247,
                BattlefieldBonusId: bonus!.Id));

        Assert.Equal(HttpStatusCode.Created, slotResponse.StatusCode);
        var slot = await slotResponse.Content.ReadFromJsonAsync<GameTimeSlotDto>();
        Assert.NotNull(slot);
        Assert.Equal(bonus.Id, slot.BattlefieldBonusId);
        Assert.Equal("Lesní výhoda", slot.BattlefieldBonusName);
    }

    [Fact]
    public async Task CreateSlot_WithoutStage_DefaultsToStart()
    {
        var game = await CreateTestGameAsync();

        var response = await Client.PostAsJsonAsync("/api/timeline/slots",
            new CreateGameTimeSlotDto(game.Id, new DateTime(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc), 2));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<GameTimeSlotDto>();
        Assert.Equal(GameTimePhase.Start, created!.Stage);
    }

    [Fact]
    public async Task CreateSlot_WithExplicitStage_RoundTrips()
    {
        var game = await CreateTestGameAsync();

        var response = await Client.PostAsJsonAsync("/api/timeline/slots",
            new CreateGameTimeSlotDto(
                GameId: game.Id,
                StartTime: new DateTime(2026, 5, 2, 14, 0, 0, DateTimeKind.Utc),
                DurationHours: 3,
                Stage: GameTimePhase.Midgame));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<GameTimeSlotDto>();
        Assert.Equal(GameTimePhase.Midgame, created!.Stage);

        var fetched = await Client.GetFromJsonAsync<List<GameTimeSlotDto>>($"/api/timeline/slots/by-game/{game.Id}");
        Assert.Single(fetched!);
        Assert.Equal(GameTimePhase.Midgame, fetched![0].Stage);
    }

    [Fact]
    public async Task UpdateSlot_ChangeStage_Persists()
    {
        var game = await CreateTestGameAsync();
        var create = await Client.PostAsJsonAsync("/api/timeline/slots",
            new CreateGameTimeSlotDto(game.Id, new DateTime(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc), 2));
        var slot = await create.Content.ReadFromJsonAsync<GameTimeSlotDto>();

        var update = await Client.PutAsJsonAsync($"/api/timeline/slots/{slot!.Id}",
            new UpdateGameTimeSlotDto(
                StartTime: slot.StartTime,
                DurationHours: slot.DurationHours,
                InGameYear: null,
                Rules: null,
                BattlefieldBonusId: null,
                Stage: GameTimePhase.Lategame));
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);

        var fetched = await Client.GetFromJsonAsync<List<GameTimeSlotDto>>($"/api/timeline/slots/by-game/{game.Id}");
        Assert.Equal(GameTimePhase.Lategame, fetched!.Single().Stage);
    }

    [Fact]
    public async Task CreateSlot_WithLinkedEvents_RoundTrips()
    {
        var game = await CreateTestGameAsync();
        var ev1 = await CreateEventAsync(game.Id, "Bitva u brodu");
        var ev2 = await CreateEventAsync(game.Id, "Trh v Brodečku");

        var response = await Client.PostAsJsonAsync("/api/timeline/slots",
            new CreateGameTimeSlotDto(
                GameId: game.Id,
                StartTime: new DateTime(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc),
                DurationHours: 2,
                LinkedGameEventIds: [ev1.Id, ev2.Id]));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<GameTimeSlotDto>();
        Assert.Equal(2, created!.LinkedEvents.Count);
        Assert.Contains(created.LinkedEvents, e => e.Id == ev1.Id);
        Assert.Contains(created.LinkedEvents, e => e.Id == ev2.Id);

        // Each event was bootstrapped with its own placeholder slot — filter to the slot under test.
        var fetched = await Client.GetFromJsonAsync<List<GameTimeSlotDto>>($"/api/timeline/slots/by-game/{game.Id}");
        var thisSlot = fetched!.Single(s => s.Id == created.Id);
        Assert.Equal(2, thisSlot.LinkedEvents.Count);
    }

    [Fact]
    public async Task UpdateSlot_LinkedEventIds_SyncsAddAndRemove()
    {
        var game = await CreateTestGameAsync();
        var ev1 = await CreateEventAsync(game.Id, "První");
        var ev2 = await CreateEventAsync(game.Id, "Druhá");
        var ev3 = await CreateEventAsync(game.Id, "Třetí");

        var create = await Client.PostAsJsonAsync("/api/timeline/slots",
            new CreateGameTimeSlotDto(game.Id, new DateTime(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc), 2,
                LinkedGameEventIds: [ev1.Id, ev2.Id]));
        var slot = await create.Content.ReadFromJsonAsync<GameTimeSlotDto>();

        // Replace [ev1, ev2] with [ev2, ev3]: ev1 removed, ev3 added, ev2 retained.
        var update = await Client.PutAsJsonAsync($"/api/timeline/slots/{slot!.Id}",
            new UpdateGameTimeSlotDto(
                StartTime: slot.StartTime,
                DurationHours: slot.DurationHours,
                InGameYear: null,
                Rules: null,
                BattlefieldBonusId: null,
                LinkedGameEventIds: [ev2.Id, ev3.Id]));
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);

        var fetched = await Client.GetFromJsonAsync<List<GameTimeSlotDto>>($"/api/timeline/slots/by-game/{game.Id}");
        var thisSlot = fetched!.Single(s => s.Id == slot.Id);
        var linked = thisSlot.LinkedEvents.Select(e => e.Id).ToHashSet();
        Assert.Equal(new HashSet<int> { ev2.Id, ev3.Id }, linked);
    }

    [Fact]
    public async Task DeleteSlot_RemovesJoinRowsButPreservesLinkedEvent()
    {
        var game = await CreateTestGameAsync();
        var ev = await CreateEventAsync(game.Id, "Smazatelný slot");

        var create = await Client.PostAsJsonAsync("/api/timeline/slots",
            new CreateGameTimeSlotDto(game.Id, new DateTime(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc), 2,
                LinkedGameEventIds: [ev.Id]));
        var slot = await create.Content.ReadFromJsonAsync<GameTimeSlotDto>();

        var delete = await Client.DeleteAsync($"/api/timeline/slots/{slot!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        // Slot is gone.
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
        Assert.False(await db.GameTimeSlots.AnyAsync(s => s.Id == slot.Id));
        // Join rows for the deleted slot are gone.
        Assert.False(await db.GameEventTimeSlots.AnyAsync(j => j.GameTimeSlotId == slot.Id));
        // GameEvent itself is preserved (still has the placeholder-slot join row).
        Assert.True(await db.GameEvents.AnyAsync(e => e.Id == ev.Id));
    }

    [Fact]
    public async Task CreateSlot_WithCrossGameEventId_Returns400Problem()
    {
        var gameA = await CreateTestGameAsync();
        var gameB = await CreateTestGameAsync();
        var foreignEvent = await CreateEventAsync(gameB.Id, "Patří do jiné hry");

        var response = await Client.PostAsJsonAsync("/api/timeline/slots",
            new CreateGameTimeSlotDto(
                GameId: gameA.Id,
                StartTime: new DateTime(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc),
                DurationHours: 2,
                LinkedGameEventIds: [foreignEvent.Id]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("Uložení selhalo", problem!.Title);
        Assert.Contains("nepatří", problem.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateSlot_WithCrossGameEventId_Returns400Problem()
    {
        var gameA = await CreateTestGameAsync();
        var gameB = await CreateTestGameAsync();
        var foreignEvent = await CreateEventAsync(gameB.Id, "Cizí událost");

        var create = await Client.PostAsJsonAsync("/api/timeline/slots",
            new CreateGameTimeSlotDto(gameA.Id, new DateTime(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc), 2));
        var slot = await create.Content.ReadFromJsonAsync<GameTimeSlotDto>();

        var update = await Client.PutAsJsonAsync($"/api/timeline/slots/{slot!.Id}",
            new UpdateGameTimeSlotDto(
                StartTime: slot.StartTime,
                DurationHours: slot.DurationHours,
                InGameYear: null,
                Rules: null,
                BattlefieldBonusId: null,
                LinkedGameEventIds: [foreignEvent.Id]));

        Assert.Equal(HttpStatusCode.BadRequest, update.StatusCode);
        var problem = await update.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("Uložení selhalo", problem!.Title);
    }

    [Fact]
    public async Task UpdateSlot_WithNullStage_PreservesExistingStage()
    {
        var game = await CreateTestGameAsync();
        var create = await Client.PostAsJsonAsync("/api/timeline/slots",
            new CreateGameTimeSlotDto(game.Id, new DateTime(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc), 2,
                Stage: GameTimePhase.Midgame));
        var slot = await create.Content.ReadFromJsonAsync<GameTimeSlotDto>();

        // Older client: no Stage in payload — null means "leave it alone".
        var update = await Client.PutAsJsonAsync($"/api/timeline/slots/{slot!.Id}",
            new UpdateGameTimeSlotDto(
                StartTime: slot.StartTime,
                DurationHours: slot.DurationHours,
                InGameYear: 1500,
                Rules: "Aktualizovaná pravidla",
                BattlefieldBonusId: null));
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);

        var fetched = await Client.GetFromJsonAsync<List<GameTimeSlotDto>>($"/api/timeline/slots/by-game/{game.Id}");
        var thisSlot = fetched!.Single(s => s.Id == slot.Id);
        Assert.Equal(GameTimePhase.Midgame, thisSlot.Stage);
        Assert.Equal(1500, thisSlot.InGameYear);
        Assert.Equal("Aktualizovaná pravidla", thisSlot.Rules);
    }

    private int _editionSeq;

    private async Task<GameDetailDto> CreateTestGameAsync()
    {
        // Some tests create multiple games in a single run; use unique
        // (Name, Edition) pairs so any uniqueness constraint on Edition holds.
        _editionSeq++;
        var response = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto($"Test Hra {_editionSeq}", _editionSeq, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GameDetailDto>())!;
    }

    private async Task<GameEventDetailDto> CreateEventAsync(int gameId, string name)
    {
        // GameEvents enforce TimeSlotIds.Count >= 1 — bootstrap a placeholder slot for each event.
        var slotResp = await Client.PostAsJsonAsync("/api/timeline/slots",
            new CreateGameTimeSlotDto(gameId, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 1));
        slotResp.EnsureSuccessStatusCode();
        var placeholder = (await slotResp.Content.ReadFromJsonAsync<GameTimeSlotDto>())!;

        var response = await Client.PostAsJsonAsync($"/api/games/{gameId}/events",
            new CreateGameEventDto(name, null, [placeholder.Id], [], [], []));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GameEventDetailDto>())!;
    }

    // Issue #182 — confirm the new EndGame phase persists through the slot
    // create/list path. String-backed conversion handles the new value
    // without a migration; this test is the smoke that proves it.
    [Fact]
    public async Task CreateSlot_WithEndGameStage_RoundTrips()
    {
        var game = await CreateTestGameAsync();

        var response = await Client.PostAsJsonAsync("/api/timeline/slots",
            new CreateGameTimeSlotDto(
                GameId: game.Id,
                StartTime: new DateTime(2026, 5, 2, 20, 0, 0, DateTimeKind.Utc),
                DurationHours: 1,
                Stage: GameTimePhase.EndGame));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<GameTimeSlotDto>();
        Assert.Equal(GameTimePhase.EndGame, created!.Stage);

        var fetched = await Client.GetFromJsonAsync<List<GameTimeSlotDto>>(
            $"/api/timeline/slots/by-game/{game.Id}");
        Assert.NotNull(fetched);
        Assert.Contains(fetched, s => s.Stage == GameTimePhase.EndGame);
    }
}
