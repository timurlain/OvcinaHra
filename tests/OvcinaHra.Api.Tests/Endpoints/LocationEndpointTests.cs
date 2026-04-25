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

public class LocationEndpointTests(PostgresFixture postgres) : IntegrationTestBase(postgres), IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Create_ValidLocation_ReturnsCreated()
    {
        var dto = new CreateLocationDto("Bílý kámen", LocationKind.Magical, 49.75m, 17.25m,
            Description: "Magical stone in the meadow");

        var response = await Client.PostAsJsonAsync("/api/locations", dto);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<LocationDetailDto>();
        Assert.NotNull(created);
        Assert.Equal("Bílý kámen", created.Name);
        Assert.Equal(49.75m, created.Latitude);
        Assert.Equal(17.25m, created.Longitude);
        Assert.Equal(LocationKind.Magical, created.LocationKind);
    }

    [Fact]
    public async Task GetAll_ReturnsOrderedByName()
    {
        await Client.PostAsJsonAsync("/api/locations",
            new CreateLocationDto("Zelená hora", LocationKind.Wilderness, 49.5m, 17.1m));
        await Client.PostAsJsonAsync("/api/locations",
            new CreateLocationDto("Arnor", LocationKind.Town, 49.6m, 17.2m));

        var locations = await Client.GetFromJsonAsync<List<LocationListDto>>("/api/locations");
        Assert.NotNull(locations);
        Assert.Equal(2, locations.Count);
        Assert.Equal("Arnor", locations[0].Name);
        Assert.Equal("Zelená hora", locations[1].Name);
    }

    [Fact]
    public async Task AssignToGame_AndGetByGame_Works()
    {
        // Create a game
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Edition", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        // Create locations
        var loc1Response = await Client.PostAsJsonAsync("/api/locations",
            new CreateLocationDto("Loc A", LocationKind.Village, 49.5m, 17.1m));
        var loc1 = await loc1Response.Content.ReadFromJsonAsync<LocationDetailDto>();

        var loc2Response = await Client.PostAsJsonAsync("/api/locations",
            new CreateLocationDto("Loc B", LocationKind.Town, 49.6m, 17.2m));
        var loc2 = await loc2Response.Content.ReadFromJsonAsync<LocationDetailDto>();

        // Assign loc1 to game
        var assignResponse = await Client.PostAsJsonAsync("/api/locations/by-game",
            new GameLocationDto(game!.Id, loc1!.Id));
        Assert.Equal(HttpStatusCode.Created, assignResponse.StatusCode);

        // Get locations for game — only loc1
        var gameLocations = await Client.GetFromJsonAsync<List<LocationListDto>>(
            $"/api/locations/by-game/{game.Id}");
        Assert.NotNull(gameLocations);
        Assert.Single(gameLocations);
        Assert.Equal("Loc A", gameLocations[0].Name);
    }

    [Fact]
    public async Task AssignToGame_Duplicate_ReturnsConflict()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Dup Test", 1, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var locResponse = await Client.PostAsJsonAsync("/api/locations",
            new CreateLocationDto("Dup Loc", LocationKind.Village, 49.5m, 17.1m));
        var loc = await locResponse.Content.ReadFromJsonAsync<LocationDetailDto>();

        await Client.PostAsJsonAsync("/api/locations/by-game", new GameLocationDto(game!.Id, loc!.Id));
        var dupResponse = await Client.PostAsJsonAsync("/api/locations/by-game", new GameLocationDto(game.Id, loc.Id));

        Assert.Equal(HttpStatusCode.Conflict, dupResponse.StatusCode);
    }

    [Fact]
    public async Task Update_ChangesCoordinates()
    {
        var createResponse = await Client.PostAsJsonAsync("/api/locations",
            new CreateLocationDto("Movable", LocationKind.PointOfInterest, 49.0m, 17.0m));
        var created = await createResponse.Content.ReadFromJsonAsync<LocationDetailDto>();

        var updateDto = new UpdateLocationDto("Movable", LocationKind.PointOfInterest, 50.0m, 18.0m);
        var response = await Client.PutAsJsonAsync($"/api/locations/{created!.Id}", updateDto);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var updated = await Client.GetFromJsonAsync<LocationDetailDto>($"/api/locations/{created.Id}");
        Assert.Equal(50.0m, updated!.Latitude);
        Assert.Equal(18.0m, updated.Longitude);
    }

    [Fact]
    public async Task Create_WithLoreFields_ReturnsAllFields()
    {
        var dto = new CreateLocationDto("Aradhrynd", LocationKind.Town,
            Description: "Elfí palác",
            Details: "Podzemní řeky protékají komnatami",
            GamePotential: "Diplomatické mise",
            Region: "Severní Temný hvozd");

        var response = await Client.PostAsJsonAsync("/api/locations", dto);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<LocationDetailDto>();
        Assert.Equal("Elfí palác", created!.Description);
        Assert.Equal("Podzemní řeky protékají komnatami", created.Details);
        Assert.Equal("Diplomatické mise", created.GamePotential);
        Assert.Equal("Severní Temný hvozd", created.Region);
    }

    [Fact]
    public async Task Update_LoreFields_Persists()
    {
        var createResponse = await Client.PostAsJsonAsync("/api/locations",
            new CreateLocationDto("Esgaroth", LocationKind.Town));
        var created = await createResponse.Content.ReadFromJsonAsync<LocationDetailDto>();

        var updateDto = new UpdateLocationDto("Esgaroth", LocationKind.Town,
            Details: "Město na jezeře",
            GamePotential: "Obchod a politika",
            Region: "Dlouhé jezero");
        await Client.PutAsJsonAsync($"/api/locations/{created!.Id}", updateDto);

        var updated = await Client.GetFromJsonAsync<LocationDetailDto>($"/api/locations/{created.Id}");
        Assert.Equal("Město na jezeře", updated!.Details);
        Assert.Equal("Obchod a politika", updated.GamePotential);
        Assert.Equal("Dlouhé jezero", updated.Region);
    }

    [Fact]
    public async Task GetAll_IncludesRegion()
    {
        await Client.PostAsJsonAsync("/api/locations",
            new CreateLocationDto("Dol", LocationKind.PointOfInterest, Region: "Úpatí Osamělé hory"));

        var locations = await Client.GetFromJsonAsync<List<LocationListDto>>("/api/locations");
        var dol = locations!.First(l => l.Name == "Dol");
        Assert.Equal("Úpatí Osamělé hory", dol.Region);
    }

    [Fact]
    public async Task GetAll_CatalogView_StashQuestListsEmpty()
    {
        await Client.PostAsJsonAsync("/api/locations",
            new CreateLocationDto("Katalog lokace", LocationKind.PointOfInterest,
                Description: "Popis", GamePotential: "Záměr", SetupNotes: "Postavit"));

        var locations = await Client.GetFromJsonAsync<List<LocationListDto>>("/api/locations");
        var loc = locations!.First(l => l.Name == "Katalog lokace");
        Assert.Equal("Popis", loc.Description);
        Assert.Equal("Záměr", loc.GamePotential);
        Assert.Equal("Postavit", loc.SetupNotes);
        Assert.Empty(loc.Stashes);
        Assert.Empty(loc.Quests);
        Assert.Empty(loc.LocationTreasureQuests);
    }

    [Fact]
    public async Task GetByGame_LocationWithStashesAndQuests_ReturnsEnrichedDto()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Enrich Test", 1, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var locResponse = await Client.PostAsJsonAsync("/api/locations",
            new CreateLocationDto("Dol u řeky", LocationKind.PointOfInterest, 49.5m, 17.1m,
                Description: "Tichý dol",
                GamePotential: "Skrýše, setkání",
                SetupNotes: "Postavit mostek"));
        var loc = await locResponse.Content.ReadFromJsonAsync<LocationDetailDto>();

        await Client.PostAsJsonAsync("/api/locations/by-game",
            new GameLocationDto(game!.Id, loc!.Id));

        int stashId, questId, locationTreasureId, stashTreasureId, itemId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();

            var stash = new SecretStash { Name = "Skrýš u kořene", Description = "Pod kořenem starého dubu" };
            var item = new Item
            {
                Name = "Lektvar zdraví",
                ItemType = ItemType.Potion,
                ClassRequirements = new ClassRequirements(0, 0, 0, 0)
            };
            db.SecretStashes.Add(stash);
            db.Items.Add(item);
            await db.SaveChangesAsync();

            db.GameSecretStashes.Add(new GameSecretStash
            {
                GameId = game.Id,
                SecretStashId = stash.Id,
                LocationId = loc.Id
            });

            var quest = new Quest
            {
                Name = "Najít kámen",
                QuestType = QuestType.Location,
                Description = "Najít ztracený kámen",
                FullText = "Dlouhá verze úkolu",
                RewardXp = 50,
                RewardMoney = 20,
                RewardNotes = "Bonus poznámka",
                QuestRewards = [new QuestReward { ItemId = item.Id, Quantity = 2 }]
            };
            db.Quests.Add(quest);
            await db.SaveChangesAsync();

            db.QuestLocationLinks.Add(new QuestLocationLink
            {
                QuestId = quest.Id,
                LocationId = loc.Id
            });

            var locTreasure = new TreasureQuest
            {
                Title = "Poklad v dole",
                Clue = "Pod velkým kamenem",
                Difficulty = GameTimePhase.Midgame,
                LocationId = loc.Id,
                GameId = game.Id
            };
            var stashTreasure = new TreasureQuest
            {
                Title = "Poklad ve skrýši",
                Clue = "V dřevěné krabici",
                Difficulty = GameTimePhase.Early,
                SecretStashId = stash.Id,
                GameId = game.Id
            };
            db.TreasureQuests.AddRange(locTreasure, stashTreasure);
            await db.SaveChangesAsync();

            stashId = stash.Id;
            questId = quest.Id;
            locationTreasureId = locTreasure.Id;
            stashTreasureId = stashTreasure.Id;
            itemId = item.Id;
        }

        var gameLocations = await Client.GetFromJsonAsync<List<LocationListDto>>(
            $"/api/locations/by-game/{game.Id}");
        Assert.NotNull(gameLocations);
        Assert.Single(gameLocations);
        var dto = gameLocations[0];

        Assert.Equal("Dol u řeky", dto.Name);
        Assert.Equal("Tichý dol", dto.Description);
        Assert.Equal("Skrýše, setkání", dto.GamePotential);
        Assert.Equal("Postavit mostek", dto.SetupNotes);

        Assert.Single(dto.Stashes);
        var stashDto = dto.Stashes[0];
        Assert.Equal(stashId, stashDto.Id);
        Assert.Equal("Skrýš u kořene", stashDto.Name);
        // Description intentionally absent from LocationStashDto — grid doesn't need it;
        // stash edit popup fetches the full stash via /api/secret-stashes/{id}.
        Assert.Single(stashDto.TreasureQuests);
        Assert.Equal(stashTreasureId, stashDto.TreasureQuests[0].Id);
        Assert.Equal("Poklad ve skrýši", stashDto.TreasureQuests[0].Title);

        Assert.Single(dto.Quests);
        var questDto = dto.Quests[0];
        Assert.Equal(questId, questDto.Id);
        Assert.Equal("Najít kámen", questDto.Name);
        Assert.Equal(QuestType.Location, questDto.QuestType);
        Assert.Equal("Dlouhá verze úkolu", questDto.FullText);
        Assert.Equal(50, questDto.RewardXp);
        Assert.Equal(20, questDto.RewardMoney);
        Assert.Equal("Bonus poznámka", questDto.RewardNotes);
        Assert.Single(questDto.ItemRewards);
        Assert.Equal(itemId, questDto.ItemRewards[0].ItemId);
        Assert.Equal("Lektvar zdraví", questDto.ItemRewards[0].ItemName);
        Assert.Equal(2, questDto.ItemRewards[0].Quantity);

        Assert.Single(dto.LocationTreasureQuests);
        Assert.Equal(locationTreasureId, dto.LocationTreasureQuests[0].Id);
        Assert.Equal("Poklad v dole", dto.LocationTreasureQuests[0].Title);
    }

    [Fact]
    public async Task GetNearby_ReturnsLocationsWithinRadius_OrderedByDistance()
    {
        // Center: 49.5, 17.1. Near (~10 km), mid (~30 km), far (~150 km).
        await Client.PostAsJsonAsync("/api/locations",
            new CreateLocationDto("Blizko", LocationKind.Village, 49.58m, 17.12m));
        await Client.PostAsJsonAsync("/api/locations",
            new CreateLocationDto("Stredne", LocationKind.Wilderness, 49.75m, 17.15m));
        await Client.PostAsJsonAsync("/api/locations",
            new CreateLocationDto("Daleko", LocationKind.Town, 50.9m, 18.5m));

        var nearby = await Client.GetFromJsonAsync<List<NearbyLocationDto>>(
            "/api/locations/nearby?lat=49.5&lng=17.1&radiusKm=50");

        Assert.NotNull(nearby);
        Assert.Equal(2, nearby.Count);
        Assert.Equal("Blizko", nearby[0].Name);
        Assert.Equal("Stredne", nearby[1].Name);
        Assert.True(nearby[0].DistanceKm < nearby[1].DistanceKm);
    }

    [Fact]
    public async Task GetNearby_ExcludeId_DropsSubjectLocation()
    {
        var selfResp = await Client.PostAsJsonAsync("/api/locations",
            new CreateLocationDto("Self", LocationKind.Village, 49.50m, 17.10m));
        var self = await selfResp.Content.ReadFromJsonAsync<LocationDetailDto>();
        await Client.PostAsJsonAsync("/api/locations",
            new CreateLocationDto("Other", LocationKind.Village, 49.52m, 17.11m));

        var nearby = await Client.GetFromJsonAsync<List<NearbyLocationDto>>(
            $"/api/locations/nearby?lat=49.5&lng=17.1&radiusKm=10&excludeId={self!.Id}");

        Assert.NotNull(nearby);
        Assert.Single(nearby);
        Assert.Equal("Other", nearby[0].Name);
    }

    [Fact]
    public async Task GetNearby_InvalidRadius_ReturnsBadRequest()
    {
        var resp = await Client.GetAsync("/api/locations/nearby?lat=49.5&lng=17.1&radiusKm=0");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);

        var resp2 = await Client.GetAsync("/api/locations/nearby?lat=49.5&lng=17.1&radiusKm=500");
        Assert.Equal(HttpStatusCode.BadRequest, resp2.StatusCode);
    }

    [Fact]
    public async Task GetNearby_InvalidLatLng_ReturnsBadRequest()
    {
        var resp = await Client.GetAsync("/api/locations/nearby?lat=95&lng=0&radiusKm=5");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }
}
