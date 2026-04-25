using System.Net;
using System.Net.Http.Json;
using OvcinaHra.Api.Tests.Fixtures;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Tests.Endpoints;

public class NpcEndpointTests(PostgresFixture postgres) : IntegrationTestBase(postgres), IClassFixture<PostgresFixture>
{
    // --- Catalog CRUD ---

    [Fact]
    public async Task GetAll_Empty_ReturnsEmptyList()
    {
        var npcs = await Client.GetFromJsonAsync<List<NpcListDto>>("/api/npcs");
        Assert.NotNull(npcs);
        Assert.Empty(npcs);
    }

    [Fact]
    public async Task Create_ValidNpc_ReturnsCreated()
    {
        var response = await Client.PostAsJsonAsync("/api/npcs",
            new CreateNpcDto("Gandalf", NpcRole.Story, "Šedý čaroděj"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<NpcDetailDto>();
        Assert.NotNull(created);
        Assert.Equal("Gandalf", created.Name);
        Assert.Equal(NpcRole.Story, created.Role);
        Assert.Equal("Šedý čaroděj", created.Description);
    }

    [Fact]
    public async Task GetById_ExistingNpc_ReturnsNpc()
    {
        var createResponse = await Client.PostAsJsonAsync("/api/npcs",
            new CreateNpcDto("Gandalf", NpcRole.Story, "Šedý čaroděj"));
        var created = await createResponse.Content.ReadFromJsonAsync<NpcDetailDto>();

        var response = await Client.GetAsync($"/api/npcs/{created!.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var npc = await response.Content.ReadFromJsonAsync<NpcDetailDto>();
        Assert.NotNull(npc);
        Assert.Equal(created.Id, npc.Id);
        Assert.Equal("Gandalf", npc.Name);
    }

    [Fact]
    public async Task GetById_NonExistent_ReturnsNotFound()
    {
        var response = await Client.GetAsync("/api/npcs/999999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_ExistingNpc_ReturnsNoContent()
    {
        var createResponse = await Client.PostAsJsonAsync("/api/npcs",
            new CreateNpcDto("Gandalf", NpcRole.Story, "Šedý čaroděj"));
        var created = await createResponse.Content.ReadFromJsonAsync<NpcDetailDto>();

        var response = await Client.PutAsJsonAsync($"/api/npcs/{created!.Id}",
            new UpdateNpcDto("Gandalf Bílý", NpcRole.Fate, "Bílý čaroděj", null));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithBirthAndDeathYears_PersistsYears()
    {
        var response = await Client.PostAsJsonAsync("/api/npcs",
            new CreateNpcDto("Boromir", NpcRole.Story, "Syn Denethora",
                BirthYear: 2978, DeathYear: 3019));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<NpcDetailDto>();
        Assert.NotNull(created);
        Assert.Equal(2978, created.BirthYear);
        Assert.Equal(3019, created.DeathYear);

        var fetched = await Client.GetFromJsonAsync<NpcDetailDto>($"/api/npcs/{created.Id}");
        Assert.NotNull(fetched);
        Assert.Equal(2978, fetched.BirthYear);
        Assert.Equal(3019, fetched.DeathYear);
    }

    [Fact]
    public async Task Update_ChangesDeathYear_PersistsChange()
    {
        var createResponse = await Client.PostAsJsonAsync("/api/npcs",
            new CreateNpcDto("Théoden", NpcRole.King, "Král Rohanu", BirthYear: 2948));
        var created = (await createResponse.Content.ReadFromJsonAsync<NpcDetailDto>())!;
        Assert.Null(created.DeathYear);

        var updateResponse = await Client.PutAsJsonAsync($"/api/npcs/{created.Id}",
            new UpdateNpcDto("Théoden", NpcRole.King, "Král Rohanu", null,
                BirthYear: 2948, DeathYear: 3019));
        Assert.Equal(HttpStatusCode.NoContent, updateResponse.StatusCode);

        var fetched = await Client.GetFromJsonAsync<NpcDetailDto>($"/api/npcs/{created.Id}");
        Assert.NotNull(fetched);
        Assert.Equal(2948, fetched.BirthYear);
        Assert.Equal(3019, fetched.DeathYear);
    }

    [Fact]
    public async Task Delete_SoftDeletes_ReturnsNoContent()
    {
        var createResponse = await Client.PostAsJsonAsync("/api/npcs",
            new CreateNpcDto("Gandalf", NpcRole.Story, "Šedý čaroděj"));
        var created = await createResponse.Content.ReadFromJsonAsync<NpcDetailDto>();

        var deleteResponse = await Client.DeleteAsync($"/api/npcs/{created!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getResponse = await Client.GetAsync($"/api/npcs/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    // --- Per-game assignment ---

    [Fact]
    public async Task GetByGame_Empty_ReturnsEmptyList()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var npcs = await Client.GetFromJsonAsync<List<GameNpcDto>>($"/api/npcs/by-game/{game!.Id}");

        Assert.NotNull(npcs);
        Assert.Empty(npcs);
    }

    [Fact]
    public async Task AssignToGame_ValidNpc_ReturnsCreated()
    {
        var (game, npc) = await CreateGameAndNpc();

        var response = await Client.PostAsJsonAsync("/api/npcs/game-npc",
            new CreateGameNpcDto(game.Id, npc.Id));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var assigned = await response.Content.ReadFromJsonAsync<GameNpcDto>();
        Assert.NotNull(assigned);
        Assert.Equal(npc.Id, assigned.NpcId);
        Assert.Equal(game.Id, assigned.GameId);
    }

    [Fact]
    public async Task AssignToGame_Duplicate_ReturnsConflict()
    {
        var (game, npc) = await CreateGameAndNpc();

        await Client.PostAsJsonAsync("/api/npcs/game-npc",
            new CreateGameNpcDto(game.Id, npc.Id));
        var response = await Client.PostAsJsonAsync("/api/npcs/game-npc",
            new CreateGameNpcDto(game.Id, npc.Id));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task UpdateGameNpc_ChangesPlayer_ReturnsNoContent()
    {
        var (game, npc) = await CreateGameAndNpc();

        await Client.PostAsJsonAsync("/api/npcs/game-npc",
            new CreateGameNpcDto(game.Id, npc.Id));

        var response = await Client.PutAsJsonAsync($"/api/npcs/game-npc/{game.Id}/{npc.Id}",
            new UpdateGameNpcDto(42, "Hráč Jeden", "hrac@example.com", "Dobrý hráč"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task UnassignFromGame_ReturnsNoContent()
    {
        var (game, npc) = await CreateGameAndNpc();

        await Client.PostAsJsonAsync("/api/npcs/game-npc",
            new CreateGameNpcDto(game.Id, npc.Id));

        var response = await Client.DeleteAsync($"/api/npcs/game-npc/{game.Id}/{npc.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // --- Helper ---

    private async Task<(GameDetailDto Game, NpcDetailDto Npc)> CreateGameAndNpc()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = (await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>())!;

        var npcResponse = await Client.PostAsJsonAsync("/api/npcs",
            new CreateNpcDto("Gandalf", NpcRole.Story, "Šedý čaroděj"));
        var npc = (await npcResponse.Content.ReadFromJsonAsync<NpcDetailDto>())!;

        return (game, npc);
    }
}
