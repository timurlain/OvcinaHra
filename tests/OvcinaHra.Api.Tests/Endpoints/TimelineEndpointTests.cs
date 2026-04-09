using System.Net;
using System.Net.Http.Json;
using OvcinaHra.Api.Tests.Fixtures;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Tests.Endpoints;

public class TimelineEndpointTests(PostgresFixture postgres) : IntegrationTestBase(postgres)
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
            Duration: TimeSpan.FromHours(2),
            InGameYear: 1247);

        var response = await Client.PostAsJsonAsync("/api/timeline/slots", dto);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<GameTimeSlotDto>();
        Assert.NotNull(created);
        Assert.Equal(game.Id, created.GameId);
        Assert.Equal(new DateTime(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc), created.StartTime);
        Assert.Equal(TimeSpan.FromHours(2), created.Duration);
        Assert.Equal(1247, created.InGameYear);
    }

    [Fact]
    public async Task UpdateSlot_ReturnsNoContent()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var createResponse = await Client.PostAsJsonAsync("/api/timeline/slots",
            new CreateGameTimeSlotDto(game!.Id, new DateTime(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc), TimeSpan.FromHours(2)));
        var slot = await createResponse.Content.ReadFromJsonAsync<GameTimeSlotDto>();

        var updateDto = new UpdateGameTimeSlotDto(
            StartTime: new DateTime(2026, 5, 1, 14, 0, 0, DateTimeKind.Utc),
            Duration: TimeSpan.FromHours(3),
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
            new CreateGameTimeSlotDto(game!.Id, new DateTime(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc), TimeSpan.FromHours(2)));
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
                Duration: TimeSpan.FromHours(2),
                InGameYear: 1247,
                BattlefieldBonusId: bonus!.Id));

        Assert.Equal(HttpStatusCode.Created, slotResponse.StatusCode);
        var slot = await slotResponse.Content.ReadFromJsonAsync<GameTimeSlotDto>();
        Assert.NotNull(slot);
        Assert.Equal(bonus.Id, slot.BattlefieldBonusId);
    }
}
