using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using OvcinaHra.Api.Tests.Fixtures;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Tests.Endpoints;

public class BuildingEndpointTests(PostgresFixture postgres) : IntegrationTestBase(postgres), IClassFixture<PostgresFixture>
{
    private static readonly byte[] ValidPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private async Task<GameDetailDto> CreateGameAsync()
    {
        var response = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        return (await response.Content.ReadFromJsonAsync<GameDetailDto>())!;
    }

    // ── Catalog (GetAll) ────────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_Empty_ReturnsEmptyList()
    {
        var buildings = await Client.GetFromJsonAsync<List<BuildingListDto>>("/api/buildings");
        Assert.NotNull(buildings);
        Assert.Empty(buildings);
    }

    [Fact]
    public async Task GetAll_AfterCreate_ReturnsBuilding()
    {
        var response = await Client.PostAsJsonAsync("/api/buildings", new CreateBuildingDto("Hradní věž"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var buildings = await Client.GetFromJsonAsync<List<BuildingListDto>>("/api/buildings");
        Assert.NotNull(buildings);
        Assert.Contains(buildings, b => b.Name == "Hradní věž");
    }

    // ── GetByGame ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByGame_Empty_ReturnsEmptyList()
    {
        var game = await CreateGameAsync();

        var buildings = await Client.GetFromJsonAsync<List<BuildingListDto>>($"/api/buildings/by-game/{game.Id}");
        Assert.NotNull(buildings);
        Assert.Empty(buildings);
    }

    [Fact]
    public async Task GetByGame_AfterAssign_ReturnsBuilding()
    {
        var game = await CreateGameAsync();

        var createResp = await Client.PostAsJsonAsync("/api/buildings", new CreateBuildingDto("Kovárna"));
        var created = await createResp.Content.ReadFromJsonAsync<BuildingDetailDto>();

        var assignResp = await Client.PostAsJsonAsync("/api/buildings/by-game",
            new GameBuildingDto(game.Id, created!.Id));
        Assert.Equal(HttpStatusCode.Created, assignResp.StatusCode);

        var buildings = await Client.GetFromJsonAsync<List<BuildingListDto>>($"/api/buildings/by-game/{game.Id}");
        Assert.NotNull(buildings);
        var b = Assert.Single(buildings);
        Assert.Equal("Kovárna", b.Name);
    }

    // ── Create ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_ValidBuilding_ReturnsCreated()
    {
        var dto = new CreateBuildingDto("Kovárna", Description: "Místní kovárna s dobrým kovářem");

        var response = await Client.PostAsJsonAsync("/api/buildings", dto);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<BuildingDetailDto>();
        Assert.NotNull(created);
        Assert.Equal("Kovárna", created.Name);
        Assert.Equal("Místní kovárna s dobrým kovářem", created.Description);
        Assert.True(created.Id > 0);
    }

    [Fact]
    public async Task Create_WithGameIdQueryParam_AutoAssigns()
    {
        var game = await CreateGameAsync();

        var response = await Client.PostAsJsonAsync($"/api/buildings?gameId={game.Id}",
            new CreateBuildingDto("Auto-chata"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var buildings = await Client.GetFromJsonAsync<List<BuildingListDto>>($"/api/buildings/by-game/{game.Id}");
        Assert.NotNull(buildings);
        Assert.Contains(buildings, b => b.Name == "Auto-chata");
    }

    // ── GetById ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetById_ExistingBuilding_ReturnsBuilding()
    {
        var createResponse = await Client.PostAsJsonAsync("/api/buildings",
            new CreateBuildingDto("Hostinec"));
        var created = await createResponse.Content.ReadFromJsonAsync<BuildingDetailDto>();

        var building = await Client.GetFromJsonAsync<BuildingDetailDto>($"/api/buildings/{created!.Id}");

        Assert.NotNull(building);
        Assert.Equal("Hostinec", building.Name);
    }

    [Fact]
    public async Task GetById_NonExistent_ReturnsNotFound()
    {
        var response = await Client.GetAsync("/api/buildings/99999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Update ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_ExistingBuilding_ReturnsNoContent()
    {
        var createResponse = await Client.PostAsJsonAsync("/api/buildings",
            new CreateBuildingDto("Stará věž"));
        var created = await createResponse.Content.ReadFromJsonAsync<BuildingDetailDto>();

        var updateDto = new UpdateBuildingDto("Nová věž", "Opravená a posílená věž", null, null, true);
        var response = await Client.PutAsJsonAsync($"/api/buildings/{created!.Id}", updateDto);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var updated = await Client.GetFromJsonAsync<BuildingDetailDto>($"/api/buildings/{created.Id}");
        Assert.Equal("Nová věž", updated!.Name);
        Assert.Equal("Opravená a posílená věž", updated.Description);
        Assert.True(updated.IsPrebuilt);
    }

    // ── Notes round-trip ────────────────────────────────────────────────────

    [Fact]
    public async Task Create_WithNotes_PersistsAndReturnsThem()
    {
        var dto = new CreateBuildingDto("Strážní věž", Notes: "Dveře se zasekávají — org má náhradní kliku.");
        var response = await Client.PostAsJsonAsync("/api/buildings", dto);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<BuildingDetailDto>();
        Assert.NotNull(created);
        Assert.Equal("Dveře se zasekávají — org má náhradní kliku.", created.Notes);

        var fetched = await Client.GetFromJsonAsync<BuildingDetailDto>($"/api/buildings/{created.Id}");
        Assert.Equal("Dveře se zasekávají — org má náhradní kliku.", fetched!.Notes);
    }

    [Fact]
    public async Task Update_ChangesNotes_PersistsNewValue()
    {
        var createResp = await Client.PostAsJsonAsync("/api/buildings",
            new CreateBuildingDto("Chata", Notes: "původní poznámka"));
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var created = await createResp.Content.ReadFromJsonAsync<BuildingDetailDto>();
        Assert.NotNull(created);

        var updateDto = new UpdateBuildingDto(created.Name, created.Description, "nová poznámka pro orgy", created.LocationId, created.IsPrebuilt);
        var updateResp = await Client.PutAsJsonAsync($"/api/buildings/{created.Id}", updateDto);
        Assert.Equal(HttpStatusCode.NoContent, updateResp.StatusCode);

        var updated = await Client.GetFromJsonAsync<BuildingDetailDto>($"/api/buildings/{created.Id}");
        Assert.Equal("nová poznámka pro orgy", updated!.Notes);
    }

    [Fact]
    public async Task GetAll_EnrichedListDto_IncludesDescriptionAndNotes()
    {
        var createResp = await Client.PostAsJsonAsync("/api/buildings",
            new CreateBuildingDto("Kovárna", Description: "Místní kovárna", Notes: "Kovář má rád medovinu."));
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var created = await createResp.Content.ReadFromJsonAsync<BuildingDetailDto>();
        Assert.NotNull(created);

        var list = await Client.GetFromJsonAsync<List<BuildingListDto>>("/api/buildings");
        var listed = Assert.Single(list!, b => b.Id == created.Id);
        Assert.Equal("Místní kovárna", listed.Description);
        Assert.Equal("Kovář má rád medovinu.", listed.Notes);
    }

    // ── Delete ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_ExistingBuilding_ReturnsNoContent()
    {
        var createResponse = await Client.PostAsJsonAsync("/api/buildings",
            new CreateBuildingDto("Zbořeniště"));
        var created = await createResponse.Content.ReadFromJsonAsync<BuildingDetailDto>();

        var response = await Client.DeleteAsync($"/api/buildings/{created!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var getResponse = await Client.GetAsync($"/api/buildings/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    // ── Assign / Unassign ───────────────────────────────────────────────────

    [Fact]
    public async Task AssignToGame_Duplicate_ReturnsConflict()
    {
        var game = await CreateGameAsync();
        var createResp = await Client.PostAsJsonAsync("/api/buildings", new CreateBuildingDto("Stáj"));
        var created = await createResp.Content.ReadFromJsonAsync<BuildingDetailDto>();

        var dto = new GameBuildingDto(game.Id, created!.Id);
        await Client.PostAsJsonAsync("/api/buildings/by-game", dto);
        var second = await Client.PostAsJsonAsync("/api/buildings/by-game", dto);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task RemoveFromGame_Assigned_ReturnsNoContentAndUnassigns()
    {
        var game = await CreateGameAsync();
        var createResp = await Client.PostAsJsonAsync("/api/buildings", new CreateBuildingDto("Pivnice"));
        var created = await createResp.Content.ReadFromJsonAsync<BuildingDetailDto>();

        await Client.PostAsJsonAsync("/api/buildings/by-game", new GameBuildingDto(game.Id, created!.Id));

        var removeResp = await Client.DeleteAsync($"/api/buildings/by-game/{game.Id}/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, removeResp.StatusCode);

        var buildings = await Client.GetFromJsonAsync<List<BuildingListDto>>($"/api/buildings/by-game/{game.Id}");
        Assert.NotNull(buildings);
        Assert.Empty(buildings);
    }

    [Fact]
    public async Task RemoveFromGame_NotAssigned_ReturnsNotFound()
    {
        var game = await CreateGameAsync();
        var response = await Client.DeleteAsync($"/api/buildings/by-game/{game.Id}/99999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task NestedAddToGame_NewLink_ReturnsCreatedAndByGameReflects()
    {
        var game = await CreateGameAsync();
        var createResp = await Client.PostAsJsonAsync("/api/buildings", new CreateBuildingDto("Tržiště"));
        var created = await createResp.Content.ReadFromJsonAsync<BuildingDetailDto>();

        var response = await Client.PostAsync($"/api/games/{game.Id}/buildings/{created!.Id}", null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var buildings = await Client.GetFromJsonAsync<List<BuildingListDto>>($"/api/buildings/by-game/{game.Id}");
        Assert.Contains(buildings!, b => b.Id == created.Id);
    }

    [Fact]
    public async Task NestedGetGameBuildings_WithImage_UsesCatalogThumbnailUrl()
    {
        var game = await CreateGameAsync();
        var createResp = await Client.PostAsJsonAsync("/api/buildings", new CreateBuildingDto("Obrazárna"));
        var created = await createResp.Content.ReadFromJsonAsync<BuildingDetailDto>();
        Assert.NotNull(created);

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(ValidPng);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", "photo.png");
        var uploadResponse = await Client.PostAsync($"/api/images/buildings/{created.Id}", content);
        Assert.Equal(HttpStatusCode.OK, uploadResponse.StatusCode);
        var refreshed = await Client.GetFromJsonAsync<BuildingDetailDto>($"/api/buildings/{created.Id}");
        Assert.NotNull(refreshed?.ImagePath);

        var linkResponse = await Client.PostAsync($"/api/games/{game.Id}/buildings/{created.Id}", null);
        Assert.Equal(HttpStatusCode.Created, linkResponse.StatusCode);

        var buildings = await Client.GetFromJsonAsync<List<BuildingListDto>>($"/api/games/{game.Id}/buildings");
        var listed = Assert.Single(buildings!, b => b.Id == created.Id);
        Assert.NotNull(listed.ImageUrl);
        Assert.Contains($"/api/images/buildings/{created.Id}/thumb?size=small", listed.ImageUrl);
    }

    [Fact]
    public async Task NestedAddToGame_Duplicate_ReturnsOkWithoutDuplicate()
    {
        var game = await CreateGameAsync();
        var createResp = await Client.PostAsJsonAsync("/api/buildings", new CreateBuildingDto("Sýpka"));
        var created = await createResp.Content.ReadFromJsonAsync<BuildingDetailDto>();

        await Client.PostAsync($"/api/games/{game.Id}/buildings/{created!.Id}", null);
        var duplicate = await Client.PostAsync($"/api/games/{game.Id}/buildings/{created.Id}", null);

        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        var buildings = await Client.GetFromJsonAsync<List<BuildingListDto>>($"/api/games/{game.Id}/buildings");
        Assert.Single(buildings!, b => b.Id == created.Id);
    }

    [Fact]
    public async Task NestedRemoveFromGame_RemovesLinkButKeepsCatalogRecord()
    {
        var game = await CreateGameAsync();
        var createResp = await Client.PostAsJsonAsync("/api/buildings", new CreateBuildingDto("Stáje"));
        var created = await createResp.Content.ReadFromJsonAsync<BuildingDetailDto>();
        await Client.PostAsync($"/api/games/{game.Id}/buildings/{created!.Id}", null);

        var response = await Client.DeleteAsync($"/api/games/{game.Id}/buildings/{created.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var byGame = await Client.GetFromJsonAsync<List<BuildingListDto>>($"/api/buildings/by-game/{game.Id}");
        Assert.DoesNotContain(byGame!, b => b.Id == created.Id);
        var catalog = await Client.GetFromJsonAsync<BuildingDetailDto>($"/api/buildings/{created.Id}");
        Assert.Equal("Stáje", catalog!.Name);
    }

    [Fact]
    public async Task NestedRemoveFromGame_NotAssigned_ReturnsNotFound()
    {
        var game = await CreateGameAsync();
        var response = await Client.DeleteAsync($"/api/games/{game.Id}/buildings/9999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Location ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_WithLocation_SetsLocationId()
    {
        var game = await CreateGameAsync();

        var locationResponse = await Client.PostAsJsonAsync("/api/locations",
            new CreateLocationDto("Hora", LocationKind.Wilderness, 49.5m, 17.1m));
        var location = await locationResponse.Content.ReadFromJsonAsync<LocationDetailDto>();

        var dto = new CreateBuildingDto("Horská chata", LocationId: location!.Id);
        var response = await Client.PostAsJsonAsync($"/api/buildings?gameId={game.Id}", dto);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<BuildingDetailDto>();
        Assert.NotNull(created);
        Assert.Equal(location.Id, created.LocationId);

        var listed = await Client.GetFromJsonAsync<List<BuildingListDto>>($"/api/buildings/by-game/{game.Id}");
        Assert.NotNull(listed);
        var listedBuilding = Assert.Single(listed);
        Assert.Equal(location.Id, listedBuilding.LocationId);
        Assert.Equal("Hora", listedBuilding.LocationName);
    }
}
