using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Services;
using OvcinaHra.Api.Tests.Fixtures;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Domain.ValueObjects;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Tests.Endpoints;

public class ExportEndpointTests(PostgresFixture postgres)
    : IntegrationTestBase(postgres), IClassFixture<PostgresFixture>
{
    private async Task<GameDetailDto> CreateGameAsync()
    {
        var response = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Edition 30", 30, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GameDetailDto>())!;
    }

    [Fact]
    public async Task ExplorerMap_MissingBoundingBox_ReturnsCzechProblemDetails()
    {
        var game = await CreateGameAsync();

        var response = await Client.GetAsync(
            $"/api/games/{game.Id}/exports/explorer-map.pdf?style=blank");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("Hra nemá nastavené hranice mapy.", problem.Detail);
    }

    [Fact]
    public async Task ExplorerMap_UnknownBasemap_ReturnsProblemDetails()
    {
        var game = await CreateGameAsync();

        var response = await Client.GetAsync(
            $"/api/games/{game.Id}/exports/explorer-map.pdf?style=99");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("Neznámý podklad mapy.", problem.Detail);
    }

    [Fact]
    public async Task ExplorerMap_MapExportProblem_UsesLocalizedTitleAndDetail()
    {
        var (customFactory, customClient) = await Postgres.CreateClientAsync(services =>
        {
            foreach (var descriptor in services
                .Where(d => d.ServiceType == typeof(IExplorerMapExportService))
                .ToList())
            {
                services.Remove(descriptor);
            }

            services.AddScoped<IExplorerMapExportService, MissingMapyKeyExporter>();
        });
        await using var factoryLifetime = customFactory;
        using var clientLifetime = customClient;

        var response = await customClient.GetAsync(
            "/api/games/30/exports/explorer-map.pdf?style=tourist");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("Mapy.cz API klíč není nastaven.", problem.Title);
        Assert.Equal("Kontaktujte správce systému.", problem.Detail);
    }

    [Fact]
    public async Task MapTileClient_MapyStyleWithoutApiKey_ThrowsLocalizedProblem()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["MapyCz:ApiKey"] = "" })
            .Build();
        using var httpClient = new HttpClient();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var client = new MapTileClient(
            httpClient,
            config,
            cache,
            NullLogger<MapTileClient>.Instance);

        var ex = await Assert.ThrowsAsync<MapExportProblemException>(() =>
            client.GetTileAsync(MapExportBasemapStyle.Tourist, zoom: 10, x: 1, y: 1));

        Assert.Equal("Mapy.cz API klíč není nastaven.", ex.Title);
        Assert.Equal("Kontaktujte správce systému.", ex.Detail);
    }

    [Fact]
    public async Task ExplorerMap_BlankExport_UsesTownVillageNamesAndIdsForOtherKinds()
    {
        var game = await CreateGameAsync();
        const int wildernessId = 12345;

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
            var entity = await db.Games.SingleAsync(g => g.Id == game.Id);
            entity.BoundingBoxSwLat = 49.0m;
            entity.BoundingBoxSwLng = 17.0m;
            entity.BoundingBoxNeLat = 50.0m;
            entity.BoundingBoxNeLng = 18.0m;

            var town = new Location
            {
                Name = "Town Alpha",
                LocationKind = LocationKind.Town,
                Coordinates = new GpsCoordinates(49.5m, 17.5m),
            };
            var village = new Location
            {
                Name = "Village Beta",
                LocationKind = LocationKind.Village,
                Coordinates = new GpsCoordinates(49.55m, 17.55m),
            };
            var wilderness = new Location
            {
                Id = wildernessId,
                Name = "Hidden Wild",
                LocationKind = LocationKind.Wilderness,
                Coordinates = new GpsCoordinates(49.6m, 17.6m),
            };
            var outsideTown = new Location
            {
                Name = "Outside Town",
                LocationKind = LocationKind.Town,
                Coordinates = new GpsCoordinates(50.5m, 17.5m),
            };
            db.Locations.AddRange(town, village, wilderness, outsideTown);
            await db.SaveChangesAsync();

            db.GameLocations.AddRange(
                new GameLocation { GameId = game.Id, LocationId = town.Id },
                new GameLocation { GameId = game.Id, LocationId = village.Id },
                new GameLocation { GameId = game.Id, LocationId = wilderness.Id },
                new GameLocation { GameId = game.Id, LocationId = outsideTown.Id });
            await db.SaveChangesAsync();
        }

        var response = await Client.GetAsync(
            $"/api/games/{game.Id}/exports/explorer-map.pdf?style=blank");

        response.EnsureSuccessStatusCode();
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.StartsWith("%PDF-1.4", Encoding.ASCII.GetString(bytes, 0, 8));
        var pdfText = Encoding.ASCII.GetString(bytes);
        Assert.Contains("Town Alpha", pdfText);
        Assert.Contains("Village Beta", pdfText);
        Assert.Contains($"({wildernessId})", pdfText);
        Assert.DoesNotContain("Hidden Wild", pdfText);
        Assert.DoesNotContain("Outside Town", pdfText);
    }

    private sealed class MissingMapyKeyExporter : IExplorerMapExportService
    {
        public Task<ExplorerMapExportFile> RenderExplorerMapAsync(
            int gameId,
            MapExportBasemapStyle style,
            CancellationToken ct = default) =>
            throw new MapExportProblemException(
                "Mapy.cz API klíč není nastaven.",
                "Kontaktujte správce systému.");
    }
}
