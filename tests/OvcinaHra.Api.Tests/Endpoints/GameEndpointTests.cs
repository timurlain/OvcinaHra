using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Services;
using OvcinaHra.Api.Tests.Fixtures;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Tests.Endpoints;

public class GameEndpointTests(PostgresFixture postgres) : IntegrationTestBase(postgres), IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task GetAll_Empty_ReturnsEmptyList()
    {
        var games = await Client.GetFromJsonAsync<List<GameListDto>>("/api/games");
        Assert.NotNull(games);
        Assert.Empty(games);
    }

    [Fact]
    public async Task Create_ValidGame_ReturnsCreated()
    {
        var dto = new CreateGameDto("Balinova pozvánka", 30, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3));

        var response = await Client.PostAsJsonAsync("/api/games", dto);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<GameDetailDto>();
        Assert.NotNull(created);
        Assert.Equal("Balinova pozvánka", created.Name);
        Assert.Equal(30, created.Edition);
        Assert.Equal(GameStatus.Draft, created.Status);
        Assert.True(created.Id > 0);
    }

    [Fact]
    public async Task GetById_ExistingGame_ReturnsGame()
    {
        var dto = new CreateGameDto("Test Game", 1, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2));
        var createResponse = await Client.PostAsJsonAsync("/api/games", dto);
        var created = await createResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var game = await Client.GetFromJsonAsync<GameDetailDto>($"/api/games/{created!.Id}");

        Assert.NotNull(game);
        Assert.Equal("Test Game", game.Name);
    }

    [Fact]
    public async Task GetById_NonExistent_ReturnsNotFound()
    {
        var response = await Client.GetAsync("/api/games/99999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_ExistingGame_ReturnsNoContent()
    {
        var createDto = new CreateGameDto("Original", 1, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2));
        var createResponse = await Client.PostAsJsonAsync("/api/games", createDto);
        var created = await createResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var updateDto = new UpdateGameDto("Updated", 2, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 3), GameStatus.Active);
        var response = await Client.PutAsJsonAsync($"/api/games/{created!.Id}", updateDto);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var updated = await Client.GetFromJsonAsync<GameDetailDto>($"/api/games/{created.Id}");
        Assert.Equal("Updated", updated!.Name);
        Assert.Equal(GameStatus.Active, updated.Status);
    }

    [Fact]
    public async Task Delete_ExistingGame_IsDisabled()
    {
        var dto = new CreateGameDto("Protected", 1, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2));
        var createResponse = await Client.PostAsJsonAsync("/api/games", dto);
        var created = await createResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var response = await Client.DeleteAsync($"/api/games/{created!.Id}");
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);

        // Game still exists
        var getResponse = await Client.GetAsync($"/api/games/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
    }

    // ----- Bounding box round-trip + validation (issue #2) -----

    [Fact]
    public async Task Update_BoundingBox_RoundTripsThroughGet()
    {
        var created = await CreateGameAsync("Bbox round-trip");

        var updateDto = new UpdateGameDto(
            "Bbox round-trip", 1, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2), GameStatus.Draft,
            BoundingBoxSwLat: 49.5m, BoundingBoxSwLng: 17.1m,
            BoundingBoxNeLat: 49.7m, BoundingBoxNeLng: 17.4m);
        var response = await Client.PutAsJsonAsync($"/api/games/{created.Id}", updateDto);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var fetched = await Client.GetFromJsonAsync<GameDetailDto>($"/api/games/{created.Id}");
        Assert.Equal(49.5m, fetched!.BoundingBoxSwLat);
        Assert.Equal(17.1m, fetched.BoundingBoxSwLng);
        Assert.Equal(49.7m, fetched.BoundingBoxNeLat);
        Assert.Equal(17.4m, fetched.BoundingBoxNeLng);
    }

    [Fact]
    public async Task Update_ClearingBoundingBox_PersistsAllNulls()
    {
        var created = await CreateGameAsync("Bbox clear");
        // Seed a bbox first
        await Client.PutAsJsonAsync($"/api/games/{created.Id}", new UpdateGameDto(
            "Bbox clear", 1, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2), GameStatus.Draft,
            BoundingBoxSwLat: 49.5m, BoundingBoxSwLng: 17.1m,
            BoundingBoxNeLat: 49.7m, BoundingBoxNeLng: 17.4m));

        // Now clear it (all four null)
        var response = await Client.PutAsJsonAsync($"/api/games/{created.Id}", new UpdateGameDto(
            "Bbox clear", 1, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2), GameStatus.Draft));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var fetched = await Client.GetFromJsonAsync<GameDetailDto>($"/api/games/{created.Id}");
        Assert.Null(fetched!.BoundingBoxSwLat);
        Assert.Null(fetched.BoundingBoxSwLng);
        Assert.Null(fetched.BoundingBoxNeLat);
        Assert.Null(fetched.BoundingBoxNeLng);
    }

    [Fact]
    public async Task Update_PartialBoundingBox_ReturnsValidationProblem()
    {
        var created = await CreateGameAsync("Partial bbox");
        // Only 2 of the 4 corners — must be rejected as 400.
        var response = await Client.PutAsJsonAsync($"/api/games/{created.Id}", new UpdateGameDto(
            "Partial bbox", 1, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2), GameStatus.Draft,
            BoundingBoxSwLat: 49.5m, BoundingBoxSwLng: 17.1m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_InvertedBoundingBox_ReturnsValidationProblem()
    {
        var created = await CreateGameAsync("Inverted bbox");
        // SW.lat > NE.lat — invalid corners.
        var response = await Client.PutAsJsonAsync($"/api/games/{created.Id}", new UpdateGameDto(
            "Inverted bbox", 1, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2), GameStatus.Draft,
            BoundingBoxSwLat: 50.0m, BoundingBoxSwLng: 17.1m,
            BoundingBoxNeLat: 49.5m, BoundingBoxNeLng: 17.4m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ----- Map overlay (issue #96) ---------------------------------------

    [Fact]
    public async Task Overlay_GetWhenNotSet_Returns204()
    {
        var game = await CreateGameAsync("Overlay-None");

        var response = await Client.GetAsync($"/api/games/{game.Id}/overlay");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Overlay_PutThenGet_RoundTripsAllShapeTypes()
    {
        var game = await CreateGameAsync("Overlay-RoundTrip");
        var dto = new MapOverlayDto(new List<MapOverlayShape>
        {
            new TextShape("t1", "#242F3D", new OverlayCoord(49.5, 17.1), "Brodeček", 16),
            new FreehandShape("f1", "#8C2423", 2, new List<OverlayCoord>
            {
                new(49.50, 17.10), new(49.51, 17.11), new(49.52, 17.12)
            }),
            new PolylineShape("pl1", "#243525", 3, new List<OverlayCoord>
            {
                new(49.60, 17.20), new(49.61, 17.21)
            }),
            new RectangleShape("r1", "#504B25", "#504B25", 2,
                new OverlayCoord(49.70, 17.30), new OverlayCoord(49.72, 17.33)),
            new CircleShape("c1", "#2D5016", null, 2,
                new OverlayCoord(49.80, 17.40), 250.0),
            new PolygonShape("p1", "#B26223", "#B26223", 2, new List<OverlayCoord>
            {
                new(49.90, 17.50), new(49.91, 17.52), new(49.89, 17.52)
            }),
            // Phase 3 — icon primitive. Asset key, rotation, scale must
            // round-trip through Game.OverlayJson without schema migration.
            new IconShape("i1", "#8C2423", "flag",
                new OverlayCoord(49.95, 17.55), Rotation: 45.0, Scale: 1.5)
        });

        var putResponse = await Client.PutAsJsonAsync($"/api/games/{game.Id}/overlay", dto);
        Assert.Equal(HttpStatusCode.NoContent, putResponse.StatusCode);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
            var stored = await db.GameMapOverlays.SingleAsync(o =>
                o.GameId == game.Id && o.Audience == MapOverlayAudience.Player);
            Assert.Contains("\"t1\"", stored.OverlayJson);
        }

        var getResponse = await Client.GetAsync($"/api/games/{game.Id}/overlay");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var roundTripped = await getResponse.Content.ReadFromJsonAsync<MapOverlayDto>();
        Assert.NotNull(roundTripped);
        Assert.Equal(7, roundTripped!.Shapes.Count);

        Assert.IsType<TextShape>(roundTripped.Shapes[0]);
        var text = (TextShape)roundTripped.Shapes[0];
        Assert.Equal("Brodeček", text.Text);
        Assert.Equal(16, text.FontSize);

        Assert.IsType<FreehandShape>(roundTripped.Shapes[1]);
        Assert.Equal(3, ((FreehandShape)roundTripped.Shapes[1]).Points.Count);

        Assert.IsType<PolylineShape>(roundTripped.Shapes[2]);
        Assert.IsType<RectangleShape>(roundTripped.Shapes[3]);

        Assert.IsType<CircleShape>(roundTripped.Shapes[4]);
        Assert.Equal(250.0, ((CircleShape)roundTripped.Shapes[4]).RadiusMeters);

        Assert.IsType<PolygonShape>(roundTripped.Shapes[5]);
        Assert.Equal(3, ((PolygonShape)roundTripped.Shapes[5]).Points.Count);

        Assert.IsType<IconShape>(roundTripped.Shapes[6]);
        var icon = (IconShape)roundTripped.Shapes[6];
        Assert.Equal("flag", icon.AssetKey);
        Assert.Equal(45.0, icon.Rotation);
        Assert.Equal(1.5, icon.Scale);
        Assert.Equal(49.95, icon.Coord.Lat);
        Assert.Equal(17.55, icon.Coord.Lng);
    }

    [Fact]
    public async Task Overlay_PutCamelCaseTextShape_RoundTripsText()
    {
        var game = await CreateGameAsync("Overlay-CamelText");
        var payload = new
        {
            shapes = new[]
            {
                new
                {
                    type = "text",
                    id = "txt-1",
                    color = "#242F3D",
                    coord = new { lat = 49.5, lng = 17.1 },
                    text = "Žluťoučký kůň",
                    fontSize = 18
                }
            }
        };

        var putResponse = await Client.PutAsJsonAsync($"/api/games/{game.Id}/overlay", payload);
        Assert.Equal(HttpStatusCode.NoContent, putResponse.StatusCode);

        var getResponse = await Client.GetAsync($"/api/games/{game.Id}/overlay");
        getResponse.EnsureSuccessStatusCode();
        var roundTripped = await getResponse.Content.ReadFromJsonAsync<MapOverlayDto>();
        var text = Assert.IsType<TextShape>(Assert.Single(roundTripped!.Shapes));
        Assert.Equal("Žluťoučký kůň", text.Text);
        Assert.Equal(18, text.FontSize);
        Assert.Equal(49.5, text.Coord.Lat);
        Assert.Equal(17.1, text.Coord.Lng);
    }

    [Fact]
    public async Task Overlay_PutOversized_Returns400()
    {
        var game = await CreateGameAsync("Overlay-Oversize");

        // Generate enough freehand points to blow past the 256 KiB cap.
        // Each OverlayCoord serialises to ~28 bytes, so 20_000 points is
        // comfortably over the limit.
        var points = new List<OverlayCoord>(20_000);
        for (int i = 0; i < 20_000; i++)
            points.Add(new OverlayCoord(49.0 + i * 1e-7, 17.0 + i * 1e-7));
        var dto = new MapOverlayDto(new List<MapOverlayShape>
        {
            new FreehandShape("huge", "#242F3D", 2, points)
        });

        var response = await Client.PutAsJsonAsync($"/api/games/{game.Id}/overlay", dto);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("Překryv je příliš velký", problem!.Title);
        Assert.Contains("KiB", problem.Detail);
    }

    [Fact]
    public async Task Overlay_GameNotFound_Returns404()
    {
        var dto = new MapOverlayDto(new List<MapOverlayShape>
        {
            new TextShape("t1", "#242F3D", new OverlayCoord(49.5, 17.1), "Test")
        });

        var putResponse = await Client.PutAsJsonAsync("/api/games/999999/overlay", dto);
        Assert.Equal(HttpStatusCode.NotFound, putResponse.StatusCode);

        var getResponse = await Client.GetAsync("/api/games/999999/overlay");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task Overlay_OrganizerAudience_IsSeparateFromPlayer()
    {
        var game = await CreateGameAsync("Overlay-Audiences");
        var player = new MapOverlayDto([new TextShape("p", "#242F3D", new OverlayCoord(49.5, 17.1), "Hráčská")]);
        var organizer = new MapOverlayDto([new TextShape("o", "#242F3D", new OverlayCoord(49.6, 17.2), "Organizátorská")]);

        var playerPut = await Client.PutAsJsonAsync($"/api/games/{game.Id}/overlay", player);
        var organizerPut = await Client.PutAsJsonAsync($"/api/games/{game.Id}/overlay?audience=organizer", organizer);

        Assert.Equal(HttpStatusCode.NoContent, playerPut.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, organizerPut.StatusCode);

        var playerGet = await Client.GetFromJsonAsync<MapOverlayDto>($"/api/games/{game.Id}/overlay?audience=player");
        var organizerGet = await Client.GetFromJsonAsync<MapOverlayDto>($"/api/games/{game.Id}/overlay?audience=organizer");
        Assert.Equal("Hráčská", Assert.IsType<TextShape>(Assert.Single(playerGet!.Shapes)).Text);
        Assert.Equal("Organizátorská", Assert.IsType<TextShape>(Assert.Single(organizerGet!.Shapes)).Text);
    }

    [Fact]
    public async Task Overlay_OrganizerAudience_NonOrganizerGetsForbidden()
    {
        var game = await CreateGameAsync("Overlay-Forbidden");
        using var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            TestJwt.CreateToken(Factory.Services, "Player"));

        var response = await client.PutAsJsonAsync(
            $"/api/games/{game.Id}/overlay?audience=organizer",
            new MapOverlayDto([]));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("Pouze organizátoři mohou pracovat s organizátorským překryvem.", problem.Detail);
    }

    [Fact]
    public async Task GetStamps_GameNotFound_Returns404()
    {
        var response = await Client.GetAsync("/api/games/999999/stamps");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetStamps_ReturnsStampedLocationsWithGameStashes()
    {
        var game = await CreateGameAsync("Stamp aggregate");
        var otherGame = await CreateGameAsync("Other stamp game");
        int stampedLocationId;
        int unstampedLocationId;
        string stampedPath;

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();

            var stamped = new Location
            {
                Name = "Razítkový most",
                LocationKind = LocationKind.PointOfInterest
            };
            var unstamped = new Location
            {
                Name = "Bez razítka",
                LocationKind = LocationKind.PointOfInterest
            };
            db.Locations.AddRange(stamped, unstamped);
            await db.SaveChangesAsync();

            stamped.StampImagePath = $"locationstamps/{stamped.Id}/image.png";
            var firstStash = new SecretStash { Name = "Skrýš pod mostem" };
            var secondStash = new SecretStash { Name = "Skrýš v kamení" };
            var unstampedStash = new SecretStash { Name = "Skrýš bez razítka" };
            var otherGameStash = new SecretStash { Name = "Cizí skrýš" };
            db.SecretStashes.AddRange(firstStash, secondStash, unstampedStash, otherGameStash);
            await db.SaveChangesAsync();

            db.GameSecretStashes.AddRange(
                new GameSecretStash { GameId = game.Id, SecretStashId = firstStash.Id, LocationId = stamped.Id },
                new GameSecretStash { GameId = game.Id, SecretStashId = secondStash.Id, LocationId = stamped.Id },
                new GameSecretStash { GameId = game.Id, SecretStashId = unstampedStash.Id, LocationId = unstamped.Id },
                new GameSecretStash { GameId = otherGame.Id, SecretStashId = otherGameStash.Id, LocationId = stamped.Id });
            await db.SaveChangesAsync();

            stampedLocationId = stamped.Id;
            unstampedLocationId = unstamped.Id;
            stampedPath = stamped.StampImagePath;
        }

        var blob = Factory.Services.GetRequiredService<IBlobStorageService>();
        await blob.UploadAsync(stampedPath, new MemoryStream([1, 2, 3]), "image/png");

        var response = await Client.GetAsync($"/api/games/{game.Id}/stamps");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stamps = await response.Content.ReadFromJsonAsync<List<GameStampDto>>();
        Assert.NotNull(stamps);
        var stamp = Assert.Single(stamps);
        Assert.Equal(stampedLocationId, stamp.LocationId);
        Assert.Equal("Razítkový most", stamp.LocationName);
        Assert.Equal($"https://fake/{stampedPath}", stamp.StampImageUrl);
        Assert.DoesNotContain(stamps, s => s.LocationId == unstampedLocationId);
        Assert.Equal(["Skrýš pod mostem", "Skrýš v kamení"], stamp.Stashes.Select(s => s.Name).ToList());
    }

    private async Task<GameDetailDto> CreateGameAsync(string name)
    {
        var dto = new CreateGameDto(name, 1, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2));
        var response = await Client.PostAsJsonAsync("/api/games", dto);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GameDetailDto>())!;
    }
}
