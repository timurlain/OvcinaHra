using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Tests.Fixtures;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Domain.ValueObjects;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Tests.Endpoints;

// Issue #207 — contract tests for the Map page endpoints.
// /api/map/data is the aggregated payload powering the page-level render;
// /api/map/locations/{id}/peek powers the right slide-in panel.
public class MapEndpointsTests(PostgresFixture postgres)
    : IntegrationTestBase(postgres), IClassFixture<PostgresFixture>
{
    private async Task<GameDetailDto> CreateGameAsync()
    {
        var response = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Map test", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GameDetailDto>())!;
    }

    [Fact]
    public async Task Data_EmptyGame_ReturnsEmptyArrays()
    {
        var game = await CreateGameAsync();

        var data = await Client.GetFromJsonAsync<MapDataDto>(
            $"/api/map/data?gameId={game.Id}");

        Assert.NotNull(data);
        Assert.Empty(data.Locations);
        Assert.Empty(data.Stashes);
    }

    [Fact]
    public async Task Data_SkipsLocationsWithoutGps()
    {
        var game = await CreateGameAsync();

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
            var withGps = new Location
            {
                Name = "Hradec",
                LocationKind = LocationKind.Wilderness,
                Coordinates = new GpsCoordinates(49.5m, 17.1m),
            };
            var withoutGps = new Location
            {
                Name = "Bez GPS",
                LocationKind = LocationKind.Wilderness,
            };
            db.Locations.AddRange(withGps, withoutGps);
            await db.SaveChangesAsync();
            db.GameLocations.AddRange(
                new GameLocation { GameId = game.Id, LocationId = withGps.Id },
                new GameLocation { GameId = game.Id, LocationId = withoutGps.Id });
            await db.SaveChangesAsync();
        }

        var data = await Client.GetFromJsonAsync<MapDataDto>(
            $"/api/map/data?gameId={game.Id}");

        Assert.NotNull(data);
        var single = Assert.Single(data.Locations);
        Assert.Equal("Hradec", single.Name);
    }

    [Fact]
    public async Task Data_SkipsVariantLocations()
    {
        // Per Location.cs comment: "Only parent locations (ParentLocationId
        // == null) appear as map pins." Variants share their parent's GPS
        // and would stack identical markers.
        var game = await CreateGameAsync();

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
            var parent = new Location
            {
                Name = "Parent",
                LocationKind = LocationKind.Wilderness,
                Coordinates = new GpsCoordinates(49.5m, 17.1m),
            };
            db.Locations.Add(parent);
            await db.SaveChangesAsync();

            var variant = new Location
            {
                Name = "Parent — varianta",
                LocationKind = LocationKind.Wilderness,
                Coordinates = new GpsCoordinates(49.5m, 17.1m),
                ParentLocationId = parent.Id,
            };
            db.Locations.Add(variant);
            await db.SaveChangesAsync();

            db.GameLocations.AddRange(
                new GameLocation { GameId = game.Id, LocationId = parent.Id },
                new GameLocation { GameId = game.Id, LocationId = variant.Id });
            await db.SaveChangesAsync();
        }

        var data = await Client.GetFromJsonAsync<MapDataDto>(
            $"/api/map/data?gameId={game.Id}");

        Assert.NotNull(data);
        var single = Assert.Single(data.Locations);
        Assert.Equal("Parent", single.Name);
    }

    [Fact]
    public async Task Data_ParentWithHobbitChildUsesChildPresentation()
    {
        var game = await CreateGameAsync();
        int parentId;

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
            var parent = new Location
            {
                Name = "Starý brod",
                LocationKind = LocationKind.Wilderness,
                Coordinates = new GpsCoordinates(49.5m, 17.1m),
            };
            db.Locations.Add(parent);
            await db.SaveChangesAsync();
            parentId = parent.Id;

            db.Locations.AddRange(
                new Location
                {
                    Name = "Dcera řeky",
                    LocationKind = LocationKind.Hobbit,
                    ParentLocationId = parent.Id,
                },
                new Location
                {
                    Name = "Druhý hobit",
                    LocationKind = LocationKind.Hobbit,
                    ParentLocationId = parent.Id,
                });
            db.GameLocations.Add(new GameLocation { GameId = game.Id, LocationId = parent.Id });
            await db.SaveChangesAsync();
        }

        var data = await Client.GetFromJsonAsync<MapDataDto>(
            $"/api/map/data?gameId={game.Id}");

        Assert.NotNull(data);
        var single = Assert.Single(data.Locations);
        Assert.Equal(parentId, single.Id);
        Assert.Equal("Starý brod", single.Name);
        Assert.Equal(LocationKind.Wilderness, single.Kind);
        Assert.Equal("Dcera řeky", single.RenderName);
        Assert.Equal(LocationKind.Hobbit, single.RenderKind);
        Assert.Equal("Dcera řeky", single.EffectiveName);
        Assert.Equal(LocationKind.Hobbit, single.EffectiveKind);
    }

    [Fact]
    public async Task Peek_NonExistentLocation_Returns404()
    {
        var game = await CreateGameAsync();

        var response = await Client.GetAsync(
            $"/api/map/locations/999999/peek?gameId={game.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Peek_LocationInDifferentGame_Returns404()
    {
        // Regression for the Copilot-caught game-scope bypass — peek
        // must enforce GameLocations membership, not just LocationId.
        var gameA = await CreateGameAsync();
        var gameB = await CreateGameAsync();
        int locationId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
            var loc = new Location
            {
                Name = "Tied to A",
                LocationKind = LocationKind.Wilderness,
                Coordinates = new GpsCoordinates(49.5m, 17.1m),
            };
            db.Locations.Add(loc);
            await db.SaveChangesAsync();
            db.GameLocations.Add(new GameLocation { GameId = gameA.Id, LocationId = loc.Id });
            await db.SaveChangesAsync();
            locationId = loc.Id;
        }

        // Peek with gameB.Id — location isn't part of that game.
        var response = await Client.GetAsync(
            $"/api/map/locations/{locationId}/peek?gameId={gameB.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Peek_AlwaysReturnsAllFourStageRows()
    {
        // Even when a location has zero treasures across the board, the
        // peek payload returns four stage rows so the layout stays stable.
        var game = await CreateGameAsync();
        int locationId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
            var loc = new Location
            {
                Name = "Empty",
                LocationKind = LocationKind.Wilderness,
                Coordinates = new GpsCoordinates(49.5m, 17.1m),
            };
            db.Locations.Add(loc);
            await db.SaveChangesAsync();
            // Link to game — the peek endpoint now joins through GameLocations
            // to enforce game scope (Copilot fixup). Pre-fixup this test
            // wouldn't have caught the cross-game leak.
            db.GameLocations.Add(new GameLocation { GameId = game.Id, LocationId = loc.Id });
            await db.SaveChangesAsync();
            locationId = loc.Id;
        }

        var peek = await Client.GetFromJsonAsync<LocationPeekDto>(
            $"/api/map/locations/{locationId}/peek?gameId={game.Id}");

        Assert.NotNull(peek);
        Assert.Equal(4, peek.TreasuresByStage.Count);
        Assert.All(peek.TreasuresByStage, r => Assert.Equal(0, r.Count));
        // Canonical stage order — Start → Early → Midgame → Lategame.
        // EndGame is intentionally excluded from the peek explorer.
        Assert.Equal(GameTimePhase.Start, peek.TreasuresByStage[0].Stage);
        Assert.Equal(GameTimePhase.Early, peek.TreasuresByStage[1].Stage);
        Assert.Equal(GameTimePhase.Midgame, peek.TreasuresByStage[2].Stage);
        Assert.Equal(GameTimePhase.Lategame, peek.TreasuresByStage[3].Stage);
    }
}
