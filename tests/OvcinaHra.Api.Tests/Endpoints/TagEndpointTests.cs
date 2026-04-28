using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using OvcinaHra.Api.Tests.Fixtures;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Tests.Endpoints;

public class TagEndpointTests(PostgresFixture postgres) : IntegrationTestBase(postgres), IClassFixture<PostgresFixture>
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

    // Issue #234 — special characters round-trip through Create end-to-end.
    // JSON body delivery means none of these need escaping; the server stores
    // the name verbatim. Theory data covers `+` (the reported regression) plus
    // the brief's smoke-test set: `&`, `:`, `'`, single space inside name.
    [Theory]
    [InlineData("C++")]
    [InlineData("1+1")]
    [InlineData("Boss & Goblin")]
    [InlineData("Trap: floor")]
    [InlineData("D'Artagnan")]
    [InlineData("Two Words")]
    public async Task Create_NameWithSpecialChars_RoundTripsVerbatim(string name)
    {
        var response = await Client.PostAsJsonAsync("/api/tags", new CreateTagDto(name, TagKind.Monster));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<TagDto>();
        Assert.Equal(name, created!.Name);

        // Also confirm the persisted value is identical when fetched back.
        var fetched = await Client.GetFromJsonAsync<TagDto>($"/api/tags/{created.Id}");
        Assert.Equal(name, fetched!.Name);
    }

    [Fact]
    public async Task Create_DuplicateNameWithinKind_ReturnsExistingTagIgnoringCase()
    {
        var initialResponse = await Client.PostAsJsonAsync("/api/tags", new CreateTagDto("big", TagKind.Monster));
        // Assert the Arrange step succeeded — without this, a regression in
        // baseline Create would still pass the test for the wrong reason.
        Assert.Equal(HttpStatusCode.Created, initialResponse.StatusCode);
        var initial = await initialResponse.Content.ReadFromJsonAsync<TagDto>();

        var response = await Client.PostAsJsonAsync("/api/tags", new CreateTagDto("BiG", TagKind.Monster));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var existing = await response.Content.ReadFromJsonAsync<TagDto>();
        Assert.Equal(initial!.Id, existing!.Id);
        Assert.Equal("big", existing.Name);

        var monsterTags = await Client.GetFromJsonAsync<List<TagDto>>("/api/tags?kind=Monster");
        Assert.Single(monsterTags!);
    }

    [Fact]
    public async Task Create_SameNameDifferentKind_CreatesSeparateTag()
    {
        var monsterResponse = await Client.PostAsJsonAsync("/api/tags", new CreateTagDto("Shared", TagKind.Monster));
        Assert.Equal(HttpStatusCode.Created, monsterResponse.StatusCode);

        var questResponse = await Client.PostAsJsonAsync("/api/tags", new CreateTagDto("shared", TagKind.Quest));
        Assert.Equal(HttpStatusCode.Created, questResponse.StatusCode);

        var monsterTag = await monsterResponse.Content.ReadFromJsonAsync<TagDto>();
        var questTag = await questResponse.Content.ReadFromJsonAsync<TagDto>();
        Assert.NotEqual(monsterTag!.Id, questTag!.Id);
        Assert.Equal(TagKind.Monster, monsterTag.Kind);
        Assert.Equal(TagKind.Quest, questTag.Kind);
    }

    [Fact]
    public async Task Create_EmptyName_Returns400()
    {
        var response = await Client.PostAsJsonAsync("/api/tags", new CreateTagDto("   ", TagKind.Monster));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Contains("prázd", problem!.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_ControlCharacter_Returns400()
    {
        var response = await Client.PostAsJsonAsync("/api/tags", new CreateTagDto("Bad\u0001Tag", TagKind.Monster));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Contains("řídicí", problem!.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_NameTrimmedOnPersist()
    {
        var response = await Client.PostAsJsonAsync("/api/tags", new CreateTagDto("  Trimmed  ", TagKind.Quest));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<TagDto>();
        Assert.Equal("Trimmed", created!.Name);
    }

    [Fact]
    public async Task Update_NameWithPlus_Persists()
    {
        var createResponse = await Client.PostAsJsonAsync("/api/tags", new CreateTagDto("Old", TagKind.Monster));
        // Assert the Arrange step (Create) succeeded — per Copilot review on PR #282.
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<TagDto>();

        var response = await Client.PutAsJsonAsync($"/api/tags/{created!.Id}", new UpdateTagDto("Foo+Bar"));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var fetched = await Client.GetFromJsonAsync<TagDto>($"/api/tags/{created.Id}");
        Assert.Equal("Foo+Bar", fetched!.Name);
    }

    [Fact]
    public async Task Update_DuplicateNameWithinKind_Returns400IgnoringCase()
    {
        var firstResponse = await Client.PostAsJsonAsync("/api/tags", new CreateTagDto("big", TagKind.Monster));
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        var secondResponse = await Client.PostAsJsonAsync("/api/tags", new CreateTagDto("small", TagKind.Monster));
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
        var second = await secondResponse.Content.ReadFromJsonAsync<TagDto>();

        var response = await Client.PutAsJsonAsync($"/api/tags/{second!.Id}", new UpdateTagDto("BiG"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Contains("už", problem!.Detail, StringComparison.OrdinalIgnoreCase);
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
