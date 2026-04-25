using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Services;
using OvcinaHra.Api.Tests.Fixtures;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Tests.Endpoints;

public class ImageEndpointTests(PostgresFixture postgres) : IntegrationTestBase(postgres), IClassFixture<PostgresFixture>
{
    // 1x1 transparent PNG
    private static readonly byte[] ValidPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    // Minimal valid JPEG (1x1 pixel). SixLabors.ImageSharp needs a real JPEG
    // header to decode — a raw byte array won't do. Kept separate from
    // ValidPng because the backfill happy-path tests need JPEGs at the
    // seeded ImagePath (which conventionally ends in .jpg).
    private static readonly byte[] ValidJpeg = Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQEASABIAAD/2wBDAAICAgICAgICAgICAgIDAwYEAwMDAwcFBQQGCAcJCQgHCAgKCw4MCgoNCwgIDBEMDQ4PEBAQCQwSExIPEw4PEBD/2wBDAQICAgMDAwYEBAYQCggKEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBD/wAARCAABAAEDASIAAhEBAxEB/8QAFQABAQAAAAAAAAAAAAAAAAAAAAn/xAAUEAEAAAAAAAAAAAAAAAAAAAAA/8QAFAEBAAAAAAAAAAAAAAAAAAAAAP/EABQRAQAAAAAAAAAAAAAAAAAAAAD/2gAMAwEAAhEDEQA/AL+AH//Z");

    [Fact]
    public async Task Upload_ValidImage_ReturnsOkWithBlobKey()
    {
        // Arrange — create a location first
        var locResponse = await Client.PostAsJsonAsync("/api/locations",
            new CreateLocationDto("Foto lokace", LocationKind.Village, 49.5m, 17.1m));
        var loc = await locResponse.Content.ReadFromJsonAsync<LocationDetailDto>();

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(ValidPng);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", "photo.png");

        // Act
        var response = await Client.PostAsync($"/api/images/locations/{loc!.Id}", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ImageUploadResult>();
        Assert.NotNull(result);
        Assert.Contains("locations/", result.BlobKey);
    }

    [Fact]
    public async Task Upload_TooLargeFile_ReturnsBadRequest()
    {
        // Arrange — create a location
        var locResponse = await Client.PostAsJsonAsync("/api/locations",
            new CreateLocationDto("Velká lokace", LocationKind.Village, 49.5m, 17.1m));
        var loc = await locResponse.Content.ReadFromJsonAsync<LocationDetailDto>();

        using var content = new MultipartFormDataContent();
        var largeFile = new byte[6 * 1024 * 1024]; // 6 MB
        var fileContent = new ByteArrayContent(largeFile);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", "huge.png");

        // Act
        var response = await Client.PostAsync($"/api/images/locations/{loc!.Id}", content);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Upload_ForBuilding_ReturnsOkAndPersistsBlobKey()
    {
        var createResp = await Client.PostAsJsonAsync("/api/buildings", new CreateBuildingDto("Foto budova"));
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var building = await createResp.Content.ReadFromJsonAsync<BuildingDetailDto>();
        Assert.NotNull(building);

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(ValidPng);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", "photo.png");

        var response = await Client.PostAsync($"/api/images/buildings/{building.Id}", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ImageUploadResult>();
        Assert.NotNull(result);
        Assert.StartsWith($"buildings/{building.Id}/", result.BlobKey);

        var refreshed = await Client.GetFromJsonAsync<BuildingDetailDto>($"/api/buildings/{building.Id}");
        Assert.NotNull(refreshed);
        Assert.Equal(result.BlobKey, refreshed.ImagePath);
    }

    [Fact]
    public async Task Upload_InvalidContentType_ReturnsBadRequest()
    {
        // Arrange — create a location
        var locResponse = await Client.PostAsJsonAsync("/api/locations",
            new CreateLocationDto("PDF lokace", LocationKind.Village, 49.5m, 17.1m));
        var loc = await locResponse.Content.ReadFromJsonAsync<LocationDetailDto>();

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent([0x25, 0x50, 0x44, 0x46]); // %PDF
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(fileContent, "file", "document.pdf");

        // Act
        var response = await Client.PostAsync($"/api/images/locations/{loc!.Id}", content);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // --- /api/images/backfill-thumbs ---

    [Fact]
    public async Task BackfillThumbs_GeneratesThumbsForAllImageBearingEntities()
    {
        // Arrange — seed one SecretStash and one Location directly via the
        // DbContext. Bypassing the upload endpoint lets us set ImagePath to a
        // predictable key, which is also the key we plant the source blob at.
        int stashId;
        int locationId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();

            var stash = new SecretStash
            {
                Name = "Skrýš pro backfill test",
                Description = "Seeded",
            };
            db.SecretStashes.Add(stash);
            await db.SaveChangesAsync();
            stash.ImagePath = $"secretstashes/{stash.Id}/image.jpg";

            var location = new Location
            {
                Name = "Lokace pro backfill test",
                LocationKind = LocationKind.Village,
            };
            db.Locations.Add(location);
            await db.SaveChangesAsync();
            location.ImagePath = $"locations/{location.Id}/image.jpg";

            await db.SaveChangesAsync();

            stashId = stash.Id;
            locationId = location.Id;
        }

        // Plant the source blobs at the keys the ThumbnailService will
        // download from. Uses the IBlobStorageService abstraction so the
        // in-memory stub stores the bytes under the exact key.
        var blob = (InMemoryBlobService)Factory.Services.GetRequiredService<IBlobStorageService>();
        await blob.UploadAsync($"secretstashes/{stashId}/image.jpg",
            new MemoryStream(ValidJpeg), "image/jpeg");
        await blob.UploadAsync($"locations/{locationId}/image.jpg",
            new MemoryStream(ValidJpeg), "image/jpeg");

        // Act
        var response = await Client.PostAsync("/api/images/backfill-thumbs", content: null);

        // Assert — response
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<ProcessedResponse>();
        Assert.NotNull(payload);
        // 2 seeded entities × 4 presets = 8. Using >= so the assertion stays
        // correct if other fixtures later seed extra image-bearing rows.
        Assert.True(payload.Processed >= 8,
            $"Expected at least 8 processed pairs, got {payload.Processed}");

        // Assert — every expected cache blob exists under the
        // {entity}-thumbs/{id}-{w}x{h}.webp layout.
        (int W, int H)[] expectedPresets =
        [
            (120, 120),
            (240, 336),
            (240, 300),
            (480, 672),
        ];
        foreach (var (w, h) in expectedPresets)
        {
            Assert.True(blob.Contains($"secretstashes-thumbs/{stashId}-{w}x{h}.webp"),
                $"expected stash thumb at secretstashes-thumbs/{stashId}-{w}x{h}.webp");
            Assert.True(blob.Contains($"locations-thumbs/{locationId}-{w}x{h}.webp"),
                $"expected location thumb at locations-thumbs/{locationId}-{w}x{h}.webp");
        }
    }

    [Fact]
    public async Task BackfillThumbs_Unauthenticated_Returns401()
    {
        // Arrange — a fresh client with no bearer token. IntegrationTestBase
        // sets the Authorization header on Client; we want a clean one here
        // so the group's RequireAuthorization() rejects us at the gate.
        using var anon = Factory.CreateClient();
        anon.DefaultRequestHeaders.Authorization = null;

        // Act
        var response = await anon.PostAsync("/api/images/backfill-thumbs", content: null);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed record ProcessedResponse(int Processed);
}
