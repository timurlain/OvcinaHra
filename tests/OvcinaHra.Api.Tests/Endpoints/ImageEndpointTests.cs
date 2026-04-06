using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using OvcinaHra.Api.Tests.Fixtures;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Tests.Endpoints;

public class ImageEndpointTests(PostgresFixture postgres) : IntegrationTestBase(postgres)
{
    // 1x1 transparent PNG
    private static readonly byte[] ValidPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

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
}
