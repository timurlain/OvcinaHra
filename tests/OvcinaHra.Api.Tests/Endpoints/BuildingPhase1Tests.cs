using System.Net;
using System.Net.Http.Json;
using OvcinaHra.Api.Tests.Fixtures;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Tests.Endpoints;

/// <summary>
/// Phase 1 of the Buildings port (issue #208): adds Building.CostMoney +
/// Building.Effect (the "build recipe" pair) plus list/detail count
/// projections (CraftingRecipesCount, GamesCount, ImageUrl). These tests
/// cover the new fields end-to-end and the projection contract that the
/// 3-mode catalog and 2-tab detail rely on.
/// </summary>
public class BuildingPhase1Tests(PostgresFixture postgres) : IntegrationTestBase(postgres), IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Create_DefaultsCostMoneyAndEffect_ToNull()
    {
        var response = await Client.PostAsJsonAsync("/api/buildings",
            new CreateBuildingDto("Bylinkářství"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<BuildingDetailDto>();
        Assert.NotNull(created);
        Assert.Equal("Bylinkářství", created.Name);
        // Phase 1 invariant: a fresh building has neither cost nor effect.
        Assert.Null(created.CostMoney);
        Assert.Null(created.Effect);
        Assert.Equal(0, created.CraftingRecipesCount);
    }

    [Fact]
    public async Task Create_WithCostAndEffect_PersistsBoth()
    {
        var response = await Client.PostAsJsonAsync("/api/buildings",
            new CreateBuildingDto(
                "Kovárna",
                Description: "Kovárna pro výrobu zbraní",
                CostMoney: 250,
                Effect: "Umožňuje výrobu kovových zbraní a brnění."));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<BuildingDetailDto>();
        Assert.NotNull(created);
        Assert.Equal(250, created.CostMoney);
        Assert.Equal("Umožňuje výrobu kovových zbraní a brnění.", created.Effect);
    }

    [Fact]
    public async Task Update_SetsCostMoneyAndEffect()
    {
        var createResponse = await Client.PostAsJsonAsync("/api/buildings",
            new CreateBuildingDto("Pivovar"));
        var created = (await createResponse.Content.ReadFromJsonAsync<BuildingDetailDto>())!;

        // Round-trip: PUT with both fields populated, then re-read and verify.
        var updateDto = new UpdateBuildingDto(
            Name: "Pivovar",
            Description: "Vaří pivo a kvas",
            Notes: null,
            LocationId: null,
            IsPrebuilt: false,
            CostMoney: 120,
            Effect: "Hráči si mohou koupit pivo za 5 grošů.");
        var putResponse = await Client.PutAsJsonAsync($"/api/buildings/{created.Id}", updateDto);
        Assert.Equal(HttpStatusCode.NoContent, putResponse.StatusCode);

        var fetched = await Client.GetFromJsonAsync<BuildingDetailDto>($"/api/buildings/{created.Id}");
        Assert.NotNull(fetched);
        Assert.Equal(120, fetched.CostMoney);
        Assert.Equal("Hráči si mohou koupit pivo za 5 grošů.", fetched.Effect);
    }

    [Fact]
    public async Task Update_ClearsCostMoneyAndEffect_BackToNull()
    {
        // Set first, then clear — confirms null is honoured (not silently
        // preserved) when the client wants to revert to "Zdarma / Bez efektu".
        var createResponse = await Client.PostAsJsonAsync("/api/buildings",
            new CreateBuildingDto(
                "Sklepení",
                CostMoney: 50,
                Effect: "Skladovací kapacita pro 10 zlaťáků."));
        var created = (await createResponse.Content.ReadFromJsonAsync<BuildingDetailDto>())!;

        var clearDto = new UpdateBuildingDto(
            Name: "Sklepení",
            Description: null, Notes: null,
            LocationId: null, IsPrebuilt: false,
            CostMoney: null, Effect: null);
        var putResponse = await Client.PutAsJsonAsync($"/api/buildings/{created.Id}", clearDto);
        Assert.Equal(HttpStatusCode.NoContent, putResponse.StatusCode);

        var fetched = await Client.GetFromJsonAsync<BuildingDetailDto>($"/api/buildings/{created.Id}");
        Assert.NotNull(fetched);
        Assert.Null(fetched.CostMoney);
        Assert.Null(fetched.Effect);
    }

    [Fact]
    public async Task GetById_PopulatesCountsAndLocationName()
    {
        // Location FK so LocationName resolves on detail.
        var locResponse = await Client.PostAsJsonAsync("/api/locations",
            new CreateLocationDto("Náměstí", LocationKind.Town, 49.0m, 17.0m));
        var location = (await locResponse.Content.ReadFromJsonAsync<LocationDetailDto>())!;

        var createResponse = await Client.PostAsJsonAsync("/api/buildings",
            new CreateBuildingDto(
                "Hostinec",
                LocationId: location.Id,
                CostMoney: 80,
                Effect: "Hráči si mohou objednat jídlo."));
        var created = (await createResponse.Content.ReadFromJsonAsync<BuildingDetailDto>())!;

        var detail = await Client.GetFromJsonAsync<BuildingDetailDto>($"/api/buildings/{created.Id}");
        Assert.NotNull(detail);
        Assert.Equal("Hostinec", detail.Name);
        Assert.Equal(location.Id, detail.LocationId);
        Assert.Equal("Náměstí", detail.LocationName);
        // No CraftingBuildingRequirement or GameBuilding linked yet.
        Assert.Equal(0, detail.CraftingRecipesCount);
        Assert.Equal(0, detail.GamesCount);
    }

    [Fact]
    public async Task GetAll_ProjectsCostMoneyAndEffectAndCounts()
    {
        await Client.PostAsJsonAsync("/api/buildings",
            new CreateBuildingDto("S efektem", CostMoney: 100, Effect: "Něco dělá."));
        await Client.PostAsJsonAsync("/api/buildings",
            new CreateBuildingDto("Bez efektu"));

        var list = await Client.GetFromJsonAsync<List<BuildingListDto>>("/api/buildings");
        Assert.NotNull(list);

        var withEffect = list.Single(b => b.Name == "S efektem");
        Assert.Equal(100, withEffect.CostMoney);
        Assert.Equal("Něco dělá.", withEffect.Effect);

        var withoutEffect = list.Single(b => b.Name == "Bez efektu");
        Assert.Null(withoutEffect.CostMoney);
        Assert.Null(withoutEffect.Effect);

        // List projection always carries the count fields, defaulting to 0
        // when no joins reference the building yet — required for the
        // BuildingTile / BuildingCardRow render paths.
        Assert.Equal(0, withoutEffect.CraftingRecipesCount);
        Assert.Equal(0, withoutEffect.GamesCount);
    }

    [Fact]
    public async Task Create_WithGameIdQuery_IncrementsGamesCount()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = (await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>())!;

        // Auto-assignment via ?gameId=N query param (existing endpoint contract).
        var createResponse = await Client.PostAsJsonAsync(
            $"/api/buildings?gameId={game.Id}",
            new CreateBuildingDto("Mlýn", CostMoney: 60, Effect: "Mele obilí na mouku."));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var created = (await createResponse.Content.ReadFromJsonAsync<BuildingDetailDto>())!;
        Assert.Equal(1, created.GamesCount);

        // Verify the same count is reflected in the list/detail re-fetches.
        var detail = await Client.GetFromJsonAsync<BuildingDetailDto>($"/api/buildings/{created.Id}");
        Assert.NotNull(detail);
        Assert.Equal(1, detail.GamesCount);
    }
}
