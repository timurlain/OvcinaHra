using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Tests.Fixtures;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Tests.Endpoints;

// Issue #214 — contract tests for the QuestWaypoint CRUD + reorder.
// Validates the (QuestId, Order) unique guard, the two-pass reorder
// update, and the Order-compaction on delete.
public class QuestWaypointEndpointTests(PostgresFixture postgres)
    : IntegrationTestBase(postgres), IClassFixture<PostgresFixture>
{
    private async Task<int> CreateQuestAsync(string name = "Trasa")
    {
        var response = await Client.PostAsJsonAsync("/api/quests",
            new CreateQuestDto(name, QuestType.General));
        response.EnsureSuccessStatusCode();
        var quest = await response.Content.ReadFromJsonAsync<QuestDetailDto>();
        return quest!.Id;
    }

    private async Task<int> CreateLocationAsync(string name)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
        var loc = new Location { Name = name, LocationKind = LocationKind.Wilderness };
        db.Locations.Add(loc);
        await db.SaveChangesAsync();
        return loc.Id;
    }

    [Fact]
    public async Task GetWaypoints_NoneSeeded_ReturnsEmpty()
    {
        var questId = await CreateQuestAsync();

        var rows = await Client.GetFromJsonAsync<List<QuestWaypointDto>>(
            $"/api/quests/{questId}/waypoints");

        Assert.NotNull(rows);
        Assert.Empty(rows);
    }

    [Fact]
    public async Task GetWaypoints_NonExistentQuest_Returns404()
    {
        var response = await Client.GetAsync("/api/quests/999999/waypoints");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AddWaypoint_AppendsAtNextOrder()
    {
        var questId = await CreateQuestAsync();
        var locA = await CreateLocationAsync("A");
        var locB = await CreateLocationAsync("B");

        var first = await Client.PostAsJsonAsync(
            $"/api/quests/{questId}/waypoints",
            new AddQuestWaypointDto(locA, "Začátek"));
        first.EnsureSuccessStatusCode();
        var firstWp = await first.Content.ReadFromJsonAsync<QuestWaypointDto>();
        Assert.Equal(1, firstWp!.Order);
        Assert.Equal("Začátek", firstWp.Label);

        var second = await Client.PostAsJsonAsync(
            $"/api/quests/{questId}/waypoints",
            new AddQuestWaypointDto(locB));
        second.EnsureSuccessStatusCode();
        var secondWp = await second.Content.ReadFromJsonAsync<QuestWaypointDto>();
        Assert.Equal(2, secondWp!.Order);
        Assert.Null(secondWp.Label);
    }

    [Fact]
    public async Task AddWaypoint_NonExistentLocation_Returns400()
    {
        var questId = await CreateQuestAsync();
        var response = await Client.PostAsJsonAsync(
            $"/api/quests/{questId}/waypoints",
            new AddQuestWaypointDto(999999));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateWaypoint_ChangesLocationAndLabel()
    {
        var questId = await CreateQuestAsync();
        var locA = await CreateLocationAsync("A");
        var locB = await CreateLocationAsync("B");
        var added = await Client.PostAsJsonAsync(
            $"/api/quests/{questId}/waypoints",
            new AddQuestWaypointDto(locA, "Old"));
        var wp = await added.Content.ReadFromJsonAsync<QuestWaypointDto>();

        var response = await Client.PutAsJsonAsync(
            $"/api/quests/{questId}/waypoints/{wp!.Id}",
            new UpdateQuestWaypointDto(locB, "New"));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var rows = await Client.GetFromJsonAsync<List<QuestWaypointDto>>(
            $"/api/quests/{questId}/waypoints");
        var single = Assert.Single(rows!);
        Assert.Equal(locB, single.LocationId);
        Assert.Equal("New", single.Label);
    }

    [Fact]
    public async Task DeleteWaypoint_CompactsRemainingOrders()
    {
        var questId = await CreateQuestAsync();
        var locA = await CreateLocationAsync("A");
        var locB = await CreateLocationAsync("B");
        var locC = await CreateLocationAsync("C");

        // Seed three waypoints — Orders 1, 2, 3.
        await Client.PostAsJsonAsync($"/api/quests/{questId}/waypoints",
            new AddQuestWaypointDto(locA));
        var second = await Client.PostAsJsonAsync($"/api/quests/{questId}/waypoints",
            new AddQuestWaypointDto(locB));
        await Client.PostAsJsonAsync($"/api/quests/{questId}/waypoints",
            new AddQuestWaypointDto(locC));
        var middleWp = await second.Content.ReadFromJsonAsync<QuestWaypointDto>();

        // Delete the middle one. Remaining should be Orders 1, 2 (not 1, 3).
        var del = await Client.DeleteAsync($"/api/quests/{questId}/waypoints/{middleWp!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var rows = await Client.GetFromJsonAsync<List<QuestWaypointDto>>(
            $"/api/quests/{questId}/waypoints");
        Assert.NotNull(rows);
        Assert.Equal(2, rows.Count);
        Assert.Equal(1, rows[0].Order);
        Assert.Equal(2, rows[1].Order);
        Assert.Equal(locA, rows[0].LocationId);
        Assert.Equal(locC, rows[1].LocationId);
    }

    [Fact]
    public async Task ReorderWaypoints_ShufflesOrderingAtomically()
    {
        var questId = await CreateQuestAsync();
        var locA = await CreateLocationAsync("A");
        var locB = await CreateLocationAsync("B");
        var locC = await CreateLocationAsync("C");

        var aResp = await Client.PostAsJsonAsync($"/api/quests/{questId}/waypoints",
            new AddQuestWaypointDto(locA));
        var bResp = await Client.PostAsJsonAsync($"/api/quests/{questId}/waypoints",
            new AddQuestWaypointDto(locB));
        var cResp = await Client.PostAsJsonAsync($"/api/quests/{questId}/waypoints",
            new AddQuestWaypointDto(locC));
        var a = (await aResp.Content.ReadFromJsonAsync<QuestWaypointDto>())!;
        var b = (await bResp.Content.ReadFromJsonAsync<QuestWaypointDto>())!;
        var c = (await cResp.Content.ReadFromJsonAsync<QuestWaypointDto>())!;

        // Target: C, A, B.
        var reorder = await Client.PutAsJsonAsync(
            $"/api/quests/{questId}/waypoints/reorder",
            new ReorderQuestWaypointsDto(new[] { c.Id, a.Id, b.Id }));
        Assert.Equal(HttpStatusCode.NoContent, reorder.StatusCode);

        var rows = await Client.GetFromJsonAsync<List<QuestWaypointDto>>(
            $"/api/quests/{questId}/waypoints");
        Assert.NotNull(rows);
        Assert.Equal(3, rows.Count);
        Assert.Equal(c.Id, rows[0].Id);
        Assert.Equal(a.Id, rows[1].Id);
        Assert.Equal(b.Id, rows[2].Id);
        // Orders compacted to 1..N.
        Assert.Equal(new[] { 1, 2, 3 }, rows.Select(r => r.Order).ToArray());
    }

    [Fact]
    public async Task ReorderWaypoints_MismatchedIdSet_Returns400()
    {
        var questId = await CreateQuestAsync();
        var loc = await CreateLocationAsync("A");
        await Client.PostAsJsonAsync($"/api/quests/{questId}/waypoints",
            new AddQuestWaypointDto(loc));

        // Send the wrong number of ids → request rejected.
        var response = await Client.PutAsJsonAsync(
            $"/api/quests/{questId}/waypoints/reorder",
            new ReorderQuestWaypointsDto(new[] { 9999 }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
