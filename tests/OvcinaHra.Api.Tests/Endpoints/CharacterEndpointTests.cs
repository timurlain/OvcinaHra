using System.Net;
using System.Net.Http.Json;
using OvcinaHra.Api.Tests.Fixtures;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Tests.Endpoints;

public class CharacterEndpointTests(PostgresFixture postgres) : IntegrationTestBase(postgres), IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task GetAll_Empty_ReturnsEmptyList()
    {
        var characters = await Client.GetFromJsonAsync<List<CharacterListDto>>("/api/characters");
        Assert.NotNull(characters);
        Assert.Empty(characters);
    }

    [Fact]
    public async Task Create_ValidCharacter_ReturnsCreated()
    {
        var response = await Client.PostAsJsonAsync("/api/characters",
            new CreateCharacterDto("Gandalf", PlayerFirstName: "Ian", PlayerLastName: "McKellen",
                Race: null, BirthYear: null, Notes: null, IsPlayedCharacter: false));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<CharacterDetailDto>();
        Assert.NotNull(created);
        Assert.Equal("Gandalf", created.Name);
        Assert.Equal("Ian", created.PlayerFirstName);
        Assert.Equal("McKellen", created.PlayerLastName);
    }

    [Fact]
    public async Task GetById_ExistingCharacter_ReturnsCharacter()
    {
        var createResponse = await Client.PostAsJsonAsync("/api/characters",
            new CreateCharacterDto("Aragorn"));
        var created = await createResponse.Content.ReadFromJsonAsync<CharacterDetailDto>();

        var response = await Client.GetAsync($"/api/characters/{created!.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var character = await response.Content.ReadFromJsonAsync<CharacterDetailDto>();
        Assert.NotNull(character);
        Assert.Equal(created.Id, character.Id);
        Assert.Equal("Aragorn", character.Name);
    }

    [Fact]
    public async Task Update_ExistingCharacter_ReturnsNoContent()
    {
        var createResponse = await Client.PostAsJsonAsync("/api/characters",
            new CreateCharacterDto("Frodo"));
        var created = await createResponse.Content.ReadFromJsonAsync<CharacterDetailDto>();

        var response = await Client.PutAsJsonAsync($"/api/characters/{created!.Id}",
            new UpdateCharacterDto("Frodo Baggins", "Elijah", "Wood", null, null, null, true, null, null));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Delete_SoftDeletes_ReturnsNoContent()
    {
        var createResponse = await Client.PostAsJsonAsync("/api/characters",
            new CreateCharacterDto("Legolas"));
        var created = await createResponse.Content.ReadFromJsonAsync<CharacterDetailDto>();

        var deleteResponse = await Client.DeleteAsync($"/api/characters/{created!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getResponse = await Client.GetAsync($"/api/characters/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task GetAll_WithSearch_FiltersResults()
    {
        await Client.PostAsJsonAsync("/api/characters", new CreateCharacterDto("Bilbo Baggins"));
        await Client.PostAsJsonAsync("/api/characters", new CreateCharacterDto("Sauron"));

        var results = await Client.GetFromJsonAsync<List<CharacterListDto>>("/api/characters?search=Bilbo");

        Assert.NotNull(results);
        Assert.Single(results);
        Assert.Equal("Bilbo Baggins", results[0].Name);
        // PlayerFullName is null when no player name set
        Assert.Null(results[0].PlayerFullName);
    }

    [Fact]
    public async Task GetAssignments_Empty_ReturnsEmptyList()
    {
        var createResponse = await Client.PostAsJsonAsync("/api/characters",
            new CreateCharacterDto("Gimli"));
        var created = await createResponse.Content.ReadFromJsonAsync<CharacterDetailDto>();

        var assignments = await Client.GetFromJsonAsync<List<CharacterAssignmentDto>>($"/api/characters/{created!.Id}/assignments");

        Assert.NotNull(assignments);
        Assert.Empty(assignments);
    }
}
