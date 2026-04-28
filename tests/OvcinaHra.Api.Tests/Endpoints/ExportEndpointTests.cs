using System.Net;
using System.Net.Http.Headers;
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
    public async Task ExplorerMap_BlankExport_UsesLabelOverlayAndIdsForOtherKinds()
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
        Assert.Contains("/SMask", pdfText);
        Assert.Contains($"({wildernessId})", pdfText);
        Assert.DoesNotContain("Hidden Wild", pdfText);
        Assert.DoesNotContain("Outside Town", pdfText);
    }

    [Fact]
    public async Task ExplorerMap_PinLabelOverlay_PreservesCzechGlyphPath()
    {
        var lines = ExplorerMapExportService.WrapPinLabelForTesting("Vořařská osada");

        Assert.Contains("ř", string.Concat(lines));
        Assert.Contains("á", string.Concat(lines));
    }

    [Fact]
    public void ExplorerMap_ReducedMargin_ExpandsMapArea()
    {
        var area = ExplorerMapExportService.CalculateMapAreaForTesting(
            MapExportPageFormat.A4Portrait,
            aspectRatio: 2.0);

        Assert.Equal(12, area.X, precision: 3);
        Assert.True(area.Width > 570);
    }

    [Fact]
    public void ExplorerMap_LongPinLabel_WrapsAcrossMultipleLines()
    {
        var lines = ExplorerMapExportService.WrapPinLabelForTesting("Caras Amarth - Karaskov");

        Assert.True(lines.Count >= 2);
        Assert.All(lines, line => Assert.True(line.Length <= 14));
    }

    [Fact]
    public void OrganizerMap_RendersNamesForAllLocationKinds()
    {
        foreach (var kind in Enum.GetValues<LocationKind>())
        {
            Assert.True(ExplorerMapExportService.ShouldRenderPinLabelForTesting(MapExportKind.Organizer, kind));
        }

        Assert.True(ExplorerMapExportService.ShouldRenderPinLabelForTesting(MapExportKind.Explorer, LocationKind.Town));
        Assert.True(ExplorerMapExportService.ShouldRenderPinLabelForTesting(MapExportKind.Kingdom, LocationKind.Village));
        Assert.False(ExplorerMapExportService.ShouldRenderPinLabelForTesting(MapExportKind.Explorer, LocationKind.Dungeon));
        Assert.False(ExplorerMapExportService.ShouldRenderPinLabelForTesting(MapExportKind.Kingdom, LocationKind.Hobbit));
    }

    [Fact]
    public async Task OrganizerMap_NonOrganizer_ReturnsCzechForbiddenProblem()
    {
        using var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            TestJwt.CreateToken(Factory.Services, "Player"));

        var response = await client.GetAsync("/api/games/30/exports/organizer-map.pdf?style=blank");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("Přístup odepřen", problem.Title);
        Assert.Equal("Pouze organizátoři mohou stáhnout tuto mapu.", problem.Detail);
    }

    [Fact]
    public async Task KingdomMap_BlankExport_UsesA3MediaBox()
    {
        var game = await CreateGameAsync();
        await SetGameBoundsAsync(game.Id);

        var response = await Client.GetAsync($"/api/games/{game.Id}/exports/kingdom-map.pdf?style=blank");

        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync();
        var pdfText = Encoding.ASCII.GetString(bytes);
        Assert.Contains("/MediaBox [0 0 841.89 1190.551]", pdfText);
        Assert.Contains("edition-30-kralovstvi-A3-", response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        WriteMapSmokeArtifact("kingdom-smoke.pdf", bytes);
    }

    [Fact]
    public async Task OrganizerMap_IncludesOrganizerOverlayButExplorerDoesNot()
    {
        var game = await CreateGameAsync();
        await SetGameBoundsAsync(game.Id);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
            db.GameMapOverlays.AddRange(
                new GameMapOverlay
                {
                    GameId = game.Id,
                    Audience = MapOverlayAudience.Player,
                    OverlayJson = """{"Shapes":[{"type":"text","Id":"p","Color":"#242F3D","Coord":{"Lat":49.5,"Lng":17.5},"Text":"PlayerOverlay","FontSize":14}]}"""
                },
                new GameMapOverlay
                {
                    GameId = game.Id,
                    Audience = MapOverlayAudience.Organizer,
                    OverlayJson = """{"Shapes":[{"type":"text","Id":"o","Color":"#242F3D","Coord":{"Lat":49.55,"Lng":17.55},"Text":"OrganizerOverlay","FontSize":14}]}"""
                });
            await db.SaveChangesAsync();
        }

        var organizer = await Client.GetAsync($"/api/games/{game.Id}/exports/organizer-map.pdf?style=blank");
        organizer.EnsureSuccessStatusCode();
        var organizerBytes = await organizer.Content.ReadAsByteArrayAsync();
        var organizerText = Encoding.ASCII.GetString(organizerBytes);
        Assert.Contains("PlayerOverlay", organizerText);
        Assert.Contains("OrganizerOverlay", organizerText);
        WriteMapSmokeArtifact("organizer-smoke.pdf", organizerBytes);

        var explorer = await Client.GetAsync($"/api/games/{game.Id}/exports/explorer-map.pdf?style=blank");
        explorer.EnsureSuccessStatusCode();
        var explorerText = Encoding.ASCII.GetString(await explorer.Content.ReadAsByteArrayAsync());
        Assert.Contains("PlayerOverlay", explorerText);
        Assert.DoesNotContain("OrganizerOverlay", explorerText);
    }

    [Fact]
    public async Task MagicBook_MissingGame_ReturnsNotFound()
    {
        var response = await Client.GetAsync("/api/games/999999/exports/magic-book.pdf");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task MagicBook_NoGameSpells_ReturnsCzechProblemDetails()
    {
        var game = await CreateGameAsync();

        var response = await Client.GetAsync($"/api/games/{game.Id}/exports/magic-book.pdf");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("Vybraná hra nemá přiřazená žádná kouzla.", problem.Detail);
    }

    [Fact]
    public async Task MagicBook_GameSpellRows_ReturnsTwoPageA4Pdf()
    {
        var game = await CreateGameAsync();

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
            var otherGame = new Game
            {
                Name = "Jiná edice",
                Edition = 31,
                StartDate = new DateOnly(2026, 6, 1),
                EndDate = new DateOnly(2026, 6, 3),
                Status = GameStatus.Active
            };
            db.Games.Add(otherGame);

            var gameSpells = Enumerable.Range(1, 20)
                .Select(i => CreateSpell($"Kouzlo {i}", ((i - 1) % 5) + 1))
                .ToList();
            var catalogOnly = CreateSpell("Jen v katalogu", 1);
            var otherSpell = CreateSpell("Cizí hra", 2);
            db.Spells.AddRange(gameSpells);
            db.Spells.AddRange(catalogOnly, otherSpell);
            await db.SaveChangesAsync();

            db.GameSpells.AddRange(gameSpells.Select(spell => new GameSpell
            {
                GameId = game.Id,
                SpellId = spell.Id
            }));
            db.GameSpells.Add(new GameSpell { GameId = otherGame.Id, SpellId = otherSpell.Id });
            await db.SaveChangesAsync();
        }

        var response = await Client.GetAsync($"/api/games/{game.Id}/exports/magic-book.pdf");

        response.EnsureSuccessStatusCode();
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("edition-30-kniha-magie-", response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.StartsWith("%PDF-1.4", Encoding.ASCII.GetString(bytes, 0, 8));
        var pdfText = Encoding.ASCII.GetString(bytes);
        Assert.Contains("/Count 2", pdfText);
        Assert.Equal(2, CountOccurrences(pdfText, "/Subtype /Image"));
        WriteMagicBookSmokeArtifact(bytes);
    }

    private sealed class MissingMapyKeyExporter : IExplorerMapExportService
    {
        public Task<ExplorerMapExportFile> RenderMapAsync(
            int gameId,
            MapExportBasemapStyle style,
            MapExportKind kind,
            MapExportPageFormat pageFormat,
            CancellationToken ct = default) =>
            throw new MapExportProblemException(
                "Mapy.cz API klíč není nastaven.",
                "Kontaktujte správce systému.");
    }

    private async Task SetGameBoundsAsync(int gameId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
        var entity = await db.Games.SingleAsync(g => g.Id == gameId);
        entity.BoundingBoxSwLat = 49.0m;
        entity.BoundingBoxSwLng = 17.0m;
        entity.BoundingBoxNeLat = 50.0m;
        entity.BoundingBoxNeLng = 18.0m;
        await db.SaveChangesAsync();
    }

    private static Spell CreateSpell(string name, int level) => new()
    {
        Name = name,
        Level = level,
        ManaCost = level,
        School = SpellSchool.Fire,
        IsScroll = false,
        IsReaction = false,
        IsLearnable = true,
        MinMageLevel = level,
        Price = level * 10,
        Effect = $"Efekt kouzla {name} s českou diakritikou: žluťoučký kůň úpěl ďábelské ódy.",
        Description = $"Popis {name}"
    };

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static void WriteMagicBookSmokeArtifact(byte[] bytes)
    {
        if (Environment.GetEnvironmentVariable("OVCINA_WRITE_MAGIC_BOOK_SMOKE") != "1")
            return;

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OvcinaHra.slnx")))
            directory = directory.Parent;

        if (directory is null)
            return;

        var tmp = Path.Combine(directory.FullName, ".tmp");
        Directory.CreateDirectory(tmp);
        File.WriteAllBytes(Path.Combine(tmp, "magic-book-A4.pdf"), bytes);
    }

    private static void WriteMapSmokeArtifact(string fileName, byte[] bytes)
    {
        if (Environment.GetEnvironmentVariable("OVCINA_WRITE_MAP_EXPORT_SMOKE") != "1")
            return;

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OvcinaHra.slnx")))
            directory = directory.Parent;

        if (directory is null)
            return;

        var tmp = Path.Combine(directory.FullName, ".tmp");
        Directory.CreateDirectory(tmp);
        File.WriteAllBytes(Path.Combine(tmp, fileName), bytes);
    }
}
