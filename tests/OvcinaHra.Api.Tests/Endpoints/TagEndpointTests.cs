using System.Net;
using System.Net.Http.Json;
using OvcinaHra.Api.Tests.Fixtures;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Tests.Endpoints;

public class TagEndpointTests(PostgresFixture postgres) : IntegrationTestBase(postgres)
{
    [Fact]
    public async Task Create_ValidTag_ReturnsCreated()
    {
        var dto = new CreateTagDto("Nebezpečný", TagKind.Monster);
        var response = await Client.PostAsJsonAsync("/api/tags", dto);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<TagDto>();
        Assert.Equal("Nebezpečný", created!.Name);
        Assert.Equal(TagKind.Monster, created.Kind);
    }

    [Fact]
    public async Task GetAll_FilterByKind_ReturnsOnlyMatchingKind()
    {
        await Client.PostAsJsonAsync("/api/tags", new CreateTagDto("Boss", TagKind.Monster));
        await Client.PostAsJsonAsync("/api/tags", new CreateTagDto("Hlavní", TagKind.Quest));

        var monsterTags = await Client.GetFromJsonAsync<List<TagDto>>("/api/tags?kind=Monster");
        Assert.NotNull(monsterTags);
        Assert.Single(monsterTags);
        Assert.Equal("Boss", monsterTags[0].Name);

        var questTags = await Client.GetFromJsonAsync<List<TagDto>>("/api/tags?kind=Quest");
        Assert.NotNull(questTags);
        Assert.Single(questTags);
        Assert.Equal("Hlavní", questTags[0].Name);
    }

    [Fact]
    public async Task Delete_ExistingTag_ReturnsNoContent()
    {
        var createResponse = await Client.PostAsJsonAsync("/api/tags", new CreateTagDto("Temp", TagKind.Monster));
        var created = await createResponse.Content.ReadFromJsonAsync<TagDto>();

        var response = await Client.DeleteAsync($"/api/tags/{created!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
