using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Tests.Fixtures;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Domain.ValueObjects;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Tests.Endpoints;

public class TreasurePlanningMissingEndpointTests(PostgresFixture postgres)
    : IntegrationTestBase(postgres), IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Missing_UsesThresholdAndExistingLocationCardRollups()
    {
        int gameId;
        int fullLocationId;
        int emptyLocationId;

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
            var game = CreateGame("Chybějící poklady");
            var fullLocation = new Location
            {
                Name = "Starý brod",
                LocationKind = LocationKind.Wilderness,
                Region = "Sever"
            };
            var emptyLocation = new Location
            {
                Name = "Prázdná louka",
                LocationKind = LocationKind.Wilderness,
                Region = "Jih"
            };
            var item = CreateItem("Rubín");
            var stash = new SecretStash { Name = "Skrýš u brodu" };

            db.AddRange(game, fullLocation, emptyLocation, item, stash);
            await db.SaveChangesAsync();

            var childLocation = new Location
            {
                Name = "Dcera řeky",
                LocationKind = LocationKind.Hobbit,
                Region = "Sever",
                ParentLocationId = fullLocation.Id
            };
            db.Locations.Add(childLocation);
            await db.SaveChangesAsync();

            db.GameLocations.AddRange(
                new GameLocation { GameId = game.Id, LocationId = fullLocation.Id },
                new GameLocation { GameId = game.Id, LocationId = childLocation.Id },
                new GameLocation { GameId = game.Id, LocationId = emptyLocation.Id });
            db.GameSecretStashes.Add(new GameSecretStash
            {
                GameId = game.Id,
                LocationId = fullLocation.Id,
                SecretStashId = stash.Id
            });
            db.TreasureQuests.AddRange(
                CreateQuest(game.Id, "Brod", GameTimePhase.Start, item.Id, 1, locationId: fullLocation.Id),
                CreateQuest(game.Id, "Dcera", GameTimePhase.Early, item.Id, 1, locationId: childLocation.Id),
                CreateQuest(game.Id, "Skrýš", GameTimePhase.Midgame, item.Id, 1, secretStashId: stash.Id));
            await db.SaveChangesAsync();

            gameId = game.Id;
            fullLocationId = fullLocation.Id;
            emptyLocationId = emptyLocation.Id;
        }

        var missingAtTwo = await Client.GetFromJsonAsync<List<MissingTreasureLocationDto>>(
            $"/api/treasure-planning/missing?gameId={gameId}&threshold=2");

        Assert.NotNull(missingAtTwo);
        var empty = Assert.Single(missingAtTwo!);
        Assert.Equal(emptyLocationId, empty.LocationId);
        Assert.Equal("Prázdná louka", empty.LocationName);
        Assert.Equal(0, empty.TotalItems);
        Assert.Equal(2, empty.MissingCount);

        var missingAtFour = await Client.GetFromJsonAsync<List<MissingTreasureLocationDto>>(
            $"/api/treasure-planning/missing?gameId={gameId}&threshold=4");

        Assert.NotNull(missingAtFour);
        var rolledUp = Assert.Single(missingAtFour!, l => l.LocationId == fullLocationId);
        Assert.Equal(3, rolledUp.TotalItems);
        Assert.Equal(1, rolledUp.MissingCount);
    }

    [Fact]
    public async Task Missing_DefaultThresholdIsOneAndScopedToGame()
    {
        int gameId;
        int otherGameLocationId;

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
            var game = CreateGame("První hra");
            var otherGame = CreateGame("Druhá hra");
            var location = new Location { Name = "Bez pokladu", LocationKind = LocationKind.Wilderness };
            var otherLocation = new Location { Name = "Cizí lokace", LocationKind = LocationKind.Wilderness };

            db.AddRange(game, otherGame, location, otherLocation);
            await db.SaveChangesAsync();

            db.GameLocations.AddRange(
                new GameLocation { GameId = game.Id, LocationId = location.Id },
                new GameLocation { GameId = otherGame.Id, LocationId = otherLocation.Id });
            await db.SaveChangesAsync();

            gameId = game.Id;
            otherGameLocationId = otherLocation.Id;
        }

        var missing = await Client.GetFromJsonAsync<List<MissingTreasureLocationDto>>(
            $"/api/treasure-planning/missing?gameId={gameId}");

        Assert.NotNull(missing);
        var row = Assert.Single(missing!);
        Assert.Equal(1, row.Threshold);
        Assert.DoesNotContain(missing!, l => l.LocationId == otherGameLocationId);
    }

    [Fact]
    public async Task Missing_RejectsNegativeThreshold()
    {
        var response = await Client.GetAsync("/api/treasure-planning/missing?gameId=1&threshold=-1");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static Game CreateGame(string name) => new()
    {
        Name = name,
        Edition = 1,
        StartDate = new DateOnly(2026, 5, 1),
        EndDate = new DateOnly(2026, 5, 3)
    };

    private static Item CreateItem(string name) => new()
    {
        Name = name,
        ItemType = ItemType.Potion,
        ClassRequirements = new ClassRequirements(0, 0, 0, 0)
    };

    private static TreasureQuest CreateQuest(
        int gameId,
        string title,
        GameTimePhase difficulty,
        int itemId,
        int count,
        int? locationId = null,
        int? secretStashId = null) => new()
        {
            Title = title,
            Difficulty = difficulty,
            GameId = gameId,
            LocationId = locationId,
            SecretStashId = secretStashId,
            TreasureItems =
            [
                new TreasureItem
                {
                    ItemId = itemId,
                    GameId = gameId,
                    Count = count
                }
            ]
        };
}
