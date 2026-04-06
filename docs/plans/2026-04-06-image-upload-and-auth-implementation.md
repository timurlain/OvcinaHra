# Image Upload & Shared Auth — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Use `hra-ovcina-tinkerer` skill for implementation context and `hra-ovcina-basher` for testing patterns.

**Goal:** Add Azure Blob Storage image upload for all entities with image fields, and OAuth2 login via Google/Microsoft/Seznam with registrace-ovčina user verification.

**Architecture:** Image upload uses `Azure.Storage.Blobs` SDK with Azurite for local dev. Auth adds server-side OAuth2 middleware; after provider callback, the API verifies the user exists in registrace via its Integration API, then issues a JWT.

**Tech Stack:** .NET 10, Azure.Storage.Blobs, Azurite (Docker), ASP.NET OAuth2 middleware, httpx for registrace API calls.

**Design doc:** `docs/plans/2026-04-06-image-upload-and-auth-design.md`

**Repo:** `C:\Users\TomášPajonk\source\repos\timurlain\ovcinahra`

---

## Part A: Image Upload

### Task 1: Add Azure.Storage.Blobs package and Azurite to dev setup

**Files:**
- Modify: `src/OvcinaHra.Api/OvcinaHra.Api.csproj`
- Create: `docker-compose.yml`
- Modify: `src/OvcinaHra.Api/appsettings.Development.json`

**Step 1: Add the NuGet package**

```bash
cd src/OvcinaHra.Api
dotnet add package Azure.Storage.Blobs
```

**Step 2: Create docker-compose.yml with postgres + azurite**

```yaml
services:
  postgres:
    image: postgres:17
    container_name: ovcinahra-postgres
    ports:
      - "5434:5432"
    environment:
      POSTGRES_USER: ovcinahra
      POSTGRES_PASSWORD: ovcinahra
      POSTGRES_DB: ovcinahra
    volumes:
      - ovcinahra-pgdata:/var/lib/postgresql/data

  azurite:
    image: mcr.microsoft.com/azure-storage/azurite
    container_name: ovcinahra-azurite
    ports:
      - "10000:10000"  # Blob
      - "10001:10001"  # Queue
      - "10002:10002"  # Table
    volumes:
      - ovcinahra-azurite:/data

volumes:
  ovcinahra-pgdata:
  ovcinahra-azurite:
```

**Step 3: Add blob connection string to appsettings.Development.json**

Add to the JSON:

```json
"BlobStorage": {
  "ConnectionString": "UseDevelopmentStorage=true",
  "ContainerName": "ovcinahra-images"
}
```

**Step 4: Start docker services and verify**

```bash
docker compose up -d
```

**Step 5: Commit**

```bash
git add docker-compose.yml src/OvcinaHra.Api/OvcinaHra.Api.csproj src/OvcinaHra.Api/appsettings.Development.json
git commit -m "feat: add Azure.Storage.Blobs package and Azurite docker setup"
```

---

### Task 2: BlobStorageService

**Files:**
- Create: `src/OvcinaHra.Api/Services/BlobStorageService.cs`
- Modify: `src/OvcinaHra.Api/Program.cs`

**Step 1: Write the service**

```csharp
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;

namespace OvcinaHra.Api.Services;

public interface IBlobStorageService
{
    Task<string> UploadAsync(string blobKey, Stream content, string contentType, CancellationToken ct = default);
    Task<string?> GetSasUrlAsync(string blobKey, CancellationToken ct = default);
    Task DeleteAsync(string blobKey, CancellationToken ct = default);
}

public class BlobStorageService : IBlobStorageService
{
    private readonly BlobContainerClient _container;

    public BlobStorageService(IConfiguration config)
    {
        var connectionString = config["BlobStorage:ConnectionString"]!;
        var containerName = config["BlobStorage:ContainerName"] ?? "ovcinahra-images";
        _container = new BlobContainerClient(connectionString, containerName);
    }

    public async Task<string> UploadAsync(string blobKey, Stream content, string contentType, CancellationToken ct)
    {
        await _container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
        var blob = _container.GetBlobClient(blobKey);
        await blob.UploadAsync(content, new BlobHttpHeaders { ContentType = contentType }, cancellationToken: ct);
        return blobKey;
    }

    public async Task<string?> GetSasUrlAsync(string blobKey, CancellationToken ct)
    {
        var blob = _container.GetBlobClient(blobKey);
        if (!await blob.ExistsAsync(ct))
            return null;

        // For Azurite (dev), SAS doesn't work — return direct URL
        // For production, generate SAS token
        if (_container.Uri.Host.Contains("127.0.0.1") || _container.Uri.Host.Contains("localhost"))
        {
            return blob.Uri.ToString();
        }

        var sasBuilder = new BlobSasBuilder
        {
            BlobContainerName = _container.Name,
            BlobName = blobKey,
            Resource = "b",
            ExpiresOn = DateTimeOffset.UtcNow.AddHours(1)
        };
        sasBuilder.SetPermissions(BlobSasPermissions.Read);
        return blob.GenerateSasUri(sasBuilder).ToString();
    }

    public async Task DeleteAsync(string blobKey, CancellationToken ct)
    {
        var blob = _container.GetBlobClient(blobKey);
        await blob.DeleteIfExistsAsync(cancellationToken: ct);
    }
}
```

**Step 2: Register in Program.cs**

Add after `builder.Services.AddProblemDetails();`:

```csharp
builder.Services.AddSingleton<IBlobStorageService, BlobStorageService>();
```

Add using:
```csharp
using OvcinaHra.Api.Services;
```

**Step 3: Build and verify**

```bash
dotnet build src/OvcinaHra.Api
```

**Step 4: Commit**

```bash
git add src/OvcinaHra.Api/Services/BlobStorageService.cs src/OvcinaHra.Api/Program.cs
git commit -m "feat: add BlobStorageService for Azure Blob Storage"
```

---

### Task 3: Image upload endpoint

**Files:**
- Create: `src/OvcinaHra.Api/Endpoints/ImageEndpoints.cs`
- Modify: `src/OvcinaHra.Api/Program.cs`
- Test: `tests/OvcinaHra.Api.Tests/Endpoints/ImageEndpointTests.cs`

**Step 1: Write the failing test**

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using OvcinaHra.Api.Tests.Fixtures;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Tests.Endpoints;

public class ImageEndpointTests(PostgresFixture postgres) : IntegrationTestBase(postgres)
{
    [Fact]
    public async Task Upload_ValidImage_ReturnsOkWithBlobKey()
    {
        // Create a location first
        var loc = await Client.PostAsJsonAsync("/api/locations",
            new CreateLocationDto("Test Loc", Shared.Domain.Enums.LocationKind.Village, 49.45m, 18.02m));
        var created = await loc.Content.ReadFromJsonAsync<LocationDetailDto>();

        // Upload a 1x1 PNG
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(png);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("image/png");
        content.Add(fileContent, "file", "test.png");

        var response = await Client.PostAsync($"/api/images/locations/{created!.Id}", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ImageUploadResult>();
        Assert.NotNull(result);
        Assert.Contains("locations/", result.BlobKey);
    }

    [Fact]
    public async Task Upload_TooLargeFile_ReturnsBadRequest()
    {
        var loc = await Client.PostAsJsonAsync("/api/locations",
            new CreateLocationDto("Test Loc2", Shared.Domain.Enums.LocationKind.Village, 49.45m, 18.02m));
        var created = await loc.Content.ReadFromJsonAsync<LocationDetailDto>();

        // 6 MB of zeros — exceeds 5 MB limit
        var bigFile = new byte[6 * 1024 * 1024];
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bigFile);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("image/png");
        content.Add(fileContent, "file", "big.png");

        var response = await Client.PostAsync($"/api/images/locations/{created!.Id}", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Upload_InvalidContentType_ReturnsBadRequest()
    {
        var loc = await Client.PostAsJsonAsync("/api/locations",
            new CreateLocationDto("Test Loc3", Shared.Domain.Enums.LocationKind.Village, 49.45m, 18.02m));
        var created = await loc.Content.ReadFromJsonAsync<LocationDetailDto>();

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(new byte[100]);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        content.Add(fileContent, "file", "doc.pdf");

        var response = await Client.PostAsync($"/api/images/locations/{created!.Id}", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
```

**Step 2: Run test to verify it fails**

```bash
dotnet test tests/OvcinaHra.Api.Tests --filter "ImageEndpointTests" --no-build
```
Expected: FAIL — endpoint doesn't exist yet.

**Step 3: Create DTOs**

Add to `src/OvcinaHra.Shared/Dtos/ImageDtos.cs`:

```csharp
namespace OvcinaHra.Shared.Dtos;

public record ImageUploadResult(string BlobKey, string? Url);
public record ImageUrlsDto(string? ImageUrl, string? PlacementUrl);
```

**Step 4: Write ImageEndpoints**

```csharp
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Services;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Endpoints;

public static class ImageEndpoints
{
    private const long MaxFileSize = 5 * 1024 * 1024; // 5 MB
    private static readonly HashSet<string> AllowedContentTypes = ["image/jpeg", "image/png", "image/webp"];

    // Entity type → (DbSet accessor, image path setter, placement path setter or null)
    private static readonly HashSet<string> ValidEntityTypes =
        ["locations", "items", "monsters", "secretstashes"];

    public static RouteGroupBuilder MapImageEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/images").WithTags("Images");

        group.MapPost("/{entityType}/{entityId:int}", Upload)
            .DisableAntiforgery();
        group.MapGet("/{entityType}/{entityId:int}", GetUrls);
        group.MapDelete("/{entityType}/{entityId:int}", Delete);

        return group;
    }

    private static async Task<Results<Ok<ImageUploadResult>, BadRequest<string>, NotFound>> Upload(
        string entityType, int entityId, IFormFile file, IBlobStorageService blobService,
        WorldDbContext db, string? field = null)
    {
        if (!ValidEntityTypes.Contains(entityType))
            return TypedResults.BadRequest($"Neplatný typ entity: {entityType}");

        if (file.Length > MaxFileSize)
            return TypedResults.BadRequest($"Soubor je příliš velký. Max {MaxFileSize / 1024 / 1024} MB.");

        if (!AllowedContentTypes.Contains(file.ContentType))
            return TypedResults.BadRequest($"Nepodporovaný formát. Povolené: JPEG, PNG, WebP.");

        var ext = file.ContentType switch
        {
            "image/jpeg" => "jpg",
            "image/png" => "png",
            "image/webp" => "webp",
            _ => "bin"
        };

        var isPlacement = field == "placement" && entityType == "locations";
        var suffix = isPlacement ? "placement" : "image";
        var blobKey = $"{entityType}/{entityId}/{suffix}.{ext}";

        using var stream = file.OpenReadStream();
        await blobService.UploadAsync(blobKey, stream, file.ContentType);

        // Update entity's ImagePath
        var updated = await UpdateEntityImagePath(db, entityType, entityId, blobKey, isPlacement);
        if (!updated)
            return TypedResults.NotFound();

        var url = await blobService.GetSasUrlAsync(blobKey);
        return TypedResults.Ok(new ImageUploadResult(blobKey, url));
    }

    private static async Task<Results<Ok<ImageUrlsDto>, NotFound>> GetUrls(
        string entityType, int entityId, IBlobStorageService blobService, WorldDbContext db)
    {
        if (!ValidEntityTypes.Contains(entityType))
            return TypedResults.NotFound();

        var (imagePath, placementPath) = await GetEntityImagePaths(db, entityType, entityId);
        if (imagePath is null && placementPath is null)
        {
            // Check entity exists
            var exists = await EntityExists(db, entityType, entityId);
            if (!exists) return TypedResults.NotFound();
            return TypedResults.Ok(new ImageUrlsDto(null, null));
        }

        var imageUrl = imagePath is not null ? await blobService.GetSasUrlAsync(imagePath) : null;
        var placementUrl = placementPath is not null ? await blobService.GetSasUrlAsync(placementPath) : null;
        return TypedResults.Ok(new ImageUrlsDto(imageUrl, placementUrl));
    }

    private static async Task<Results<NoContent, NotFound>> Delete(
        string entityType, int entityId, IBlobStorageService blobService,
        WorldDbContext db, string? field = null)
    {
        if (!ValidEntityTypes.Contains(entityType))
            return TypedResults.NotFound();

        var isPlacement = field == "placement" && entityType == "locations";
        var (imagePath, placementPath) = await GetEntityImagePaths(db, entityType, entityId);
        var pathToDelete = isPlacement ? placementPath : imagePath;

        if (pathToDelete is null)
            return TypedResults.NotFound();

        await blobService.DeleteAsync(pathToDelete);
        await UpdateEntityImagePath(db, entityType, entityId, null, isPlacement);
        return TypedResults.NoContent();
    }

    private static async Task<bool> UpdateEntityImagePath(
        WorldDbContext db, string entityType, int entityId, string? blobKey, bool isPlacement)
    {
        switch (entityType)
        {
            case "locations":
                var loc = await db.Locations.FindAsync(entityId);
                if (loc is null) return false;
                if (isPlacement) loc.PlacementPhotoPath = blobKey;
                else loc.ImagePath = blobKey;
                break;
            case "items":
                var item = await db.Items.FindAsync(entityId);
                if (item is null) return false;
                item.ImagePath = blobKey;
                break;
            case "monsters":
                var monster = await db.Monsters.FindAsync(entityId);
                if (monster is null) return false;
                monster.ImagePath = blobKey;
                break;
            case "secretstashes":
                var stash = await db.SecretStashes.FindAsync(entityId);
                if (stash is null) return false;
                stash.ImagePath = blobKey;
                break;
            default:
                return false;
        }
        await db.SaveChangesAsync();
        return true;
    }

    private static async Task<(string? ImagePath, string? PlacementPath)> GetEntityImagePaths(
        WorldDbContext db, string entityType, int entityId)
    {
        return entityType switch
        {
            "locations" => await db.Locations.Where(l => l.Id == entityId)
                .Select(l => ValueTuple.Create(l.ImagePath, l.PlacementPhotoPath))
                .FirstOrDefaultAsync(),
            "items" => await db.Items.Where(i => i.Id == entityId)
                .Select(i => ValueTuple.Create(i.ImagePath, (string?)null))
                .FirstOrDefaultAsync(),
            "monsters" => await db.Monsters.Where(m => m.Id == entityId)
                .Select(m => ValueTuple.Create(m.ImagePath, (string?)null))
                .FirstOrDefaultAsync(),
            "secretstashes" => await db.SecretStashes.Where(s => s.Id == entityId)
                .Select(s => ValueTuple.Create(s.ImagePath, (string?)null))
                .FirstOrDefaultAsync(),
            _ => (null, null)
        };
    }

    private static async Task<bool> EntityExists(WorldDbContext db, string entityType, int entityId)
    {
        return entityType switch
        {
            "locations" => await db.Locations.AnyAsync(l => l.Id == entityId),
            "items" => await db.Items.AnyAsync(i => i.Id == entityId),
            "monsters" => await db.Monsters.AnyAsync(m => m.Id == entityId),
            "secretstashes" => await db.SecretStashes.AnyAsync(s => s.Id == entityId),
            _ => false
        };
    }
}
```

**Step 5: Register in Program.cs**

Add after `app.MapSearchEndpoints().RequireAuthorization();`:

```csharp
app.MapImageEndpoints().RequireAuthorization();
```

**Step 6: Run tests**

```bash
dotnet test tests/OvcinaHra.Api.Tests --filter "ImageEndpointTests"
```

Note: Tests may need Azurite running, OR the test factory needs to register a mock blob service. If Azurite is not available in CI, add an in-memory stub in `ApiWebApplicationFactory`. The simplest path: start Azurite in docker before tests.

**Step 7: Commit**

```bash
git add src/OvcinaHra.Shared/Dtos/ImageDtos.cs src/OvcinaHra.Api/Endpoints/ImageEndpoints.cs src/OvcinaHra.Api/Program.cs tests/OvcinaHra.Api.Tests/Endpoints/ImageEndpointTests.cs
git commit -m "feat: add image upload/download/delete endpoints with blob storage"
```

---

### Task 4: Image picker UI component

**Files:**
- Create: `src/OvcinaHra.Client/Components/ImagePicker.razor`

**Step 1: Create reusable image picker component**

```razor
<div class="image-picker">
    @if (currentUrl is not null)
    {
        <div class="position-relative d-inline-block mb-2">
            <img src="@currentUrl" alt="@Alt" style="max-width: 100%; max-height: 200px; border-radius: 4px;" />
            <button class="btn btn-sm btn-danger position-absolute top-0 end-0 m-1"
                    @onclick="DeleteImage" disabled="@isWorking">✕</button>
        </div>
    }
    <InputFile OnChange="HandleFileSelected" accept=".jpg,.jpeg,.png,.webp" disabled="@isWorking" />
    @if (errorMessage is not null)
    {
        <div class="text-danger small mt-1">@errorMessage</div>
    }
</div>

@code {
    [Inject] private ApiClient Api { get; set; } = null!;

    [Parameter, EditorRequired] public string EntityType { get; set; } = "";
    [Parameter, EditorRequired] public int EntityId { get; set; }
    [Parameter] public string? Field { get; set; }
    [Parameter] public string Alt { get; set; } = "Obrázek";

    private string? currentUrl;
    private string? errorMessage;
    private bool isWorking;

    protected override async Task OnParametersSetAsync()
    {
        await LoadImageUrl();
    }

    private async Task LoadImageUrl()
    {
        if (EntityId <= 0) return;
        var urls = await Api.GetAsync<ImageUrlsDto>($"/api/images/{EntityType}/{EntityId}");
        currentUrl = Field == "placement" ? urls?.PlacementUrl : urls?.ImageUrl;
    }

    private async Task HandleFileSelected(InputFileChangeEventArgs e)
    {
        errorMessage = null;
        isWorking = true;

        try
        {
            var file = e.File;
            if (file.Size > 5 * 1024 * 1024)
            {
                errorMessage = "Soubor je příliš velký (max 5 MB).";
                return;
            }

            var query = Field is not null ? $"?field={Field}" : "";
            using var content = new MultipartFormDataContent();
            using var stream = file.OpenReadStream(maxAllowedSize: 5 * 1024 * 1024);
            var fileContent = new StreamContent(stream);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(file.ContentType);
            content.Add(fileContent, "file", file.Name);

            var result = await Api.PostMultipartAsync<ImageUploadResult>(
                $"/api/images/{EntityType}/{EntityId}{query}", content);
            if (result is not null)
                currentUrl = result.Url;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
        }
        finally
        {
            isWorking = false;
        }
    }

    private async Task DeleteImage()
    {
        isWorking = true;
        try
        {
            var query = Field is not null ? $"?field={Field}" : "";
            await Api.DeleteAsync($"/api/images/{EntityType}/{EntityId}{query}");
            currentUrl = null;
        }
        catch (Exception ex) { errorMessage = ex.Message; }
        finally { isWorking = false; }
    }
}
```

**Step 2: Add `PostMultipartAsync` to ApiClient**

In `src/OvcinaHra.Client/Services/ApiClient.cs`, add:

```csharp
public async Task<T?> PostMultipartAsync<T>(string url, MultipartFormDataContent content)
{
    var response = await _http.PostAsync(url, content);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<T>();
}
```

**Step 3: Add `using OvcinaHra.Shared.Dtos` to `_Imports.razor` if not present**

Check `src/OvcinaHra.Client/_Imports.razor` and ensure `@using OvcinaHra.Shared.Dtos` is present.

**Step 4: Build**

```bash
dotnet build src/OvcinaHra.Client
```

**Step 5: Commit**

```bash
git add src/OvcinaHra.Client/Components/ImagePicker.razor src/OvcinaHra.Client/Services/ApiClient.cs
git commit -m "feat: add reusable ImagePicker component for entity image upload"
```

---

### Task 5: Add ImagePicker to all entity edit popups

**Files:**
- Modify: `src/OvcinaHra.Client/Pages/Map/MapPage.razor` — Location (2 pickers: image + placement)
- Modify: `src/OvcinaHra.Client/Pages/Items/ItemList.razor`
- Modify: `src/OvcinaHra.Client/Pages/Monsters/MonsterList.razor`
- Modify: `src/OvcinaHra.Client/Pages/Treasures/TreasureList.razor` (SecretStash)

For each entity edit popup, add inside the `<DxFormLayout>` after the last field:

**Location (MapPage.razor):**
```razor
@if (editingId.HasValue)
{
    <DxFormLayoutItem Caption="Obrázek" ColSpanMd="12">
        <ImagePicker EntityType="locations" EntityId="editingId.Value" Alt="Ilustrace lokace" />
    </DxFormLayoutItem>
    <DxFormLayoutItem Caption="Foto umístění" ColSpanMd="12">
        <ImagePicker EntityType="locations" EntityId="editingId.Value" Field="placement" Alt="Foto umístění" />
    </DxFormLayoutItem>
}
```

**Items (ItemList.razor):**
```razor
@if (editingId.HasValue)
{
    <DxFormLayoutItem Caption="Obrázek" ColSpanMd="12">
        <ImagePicker EntityType="items" EntityId="editingId.Value" Alt="Obrázek předmětu" />
    </DxFormLayoutItem>
}
```

**Monsters (MonsterList.razor):**
```razor
@if (editingId.HasValue)
{
    <DxFormLayoutItem Caption="Obrázek" ColSpanMd="12">
        <ImagePicker EntityType="monsters" EntityId="editingId.Value" Alt="Obrázek příšery" />
    </DxFormLayoutItem>
}
```

**SecretStash (TreasureList.razor or wherever SecretStash edit is):**
```razor
@if (editingId.HasValue)
{
    <DxFormLayoutItem Caption="Obrázek" ColSpanMd="12">
        <ImagePicker EntityType="secretstashes" EntityId="editingId.Value" Alt="Ilustrace skrýše" />
    </DxFormLayoutItem>
}
```

**Step: Build and verify**
```bash
dotnet build src/OvcinaHra.Client
```

**Step: Commit**
```bash
git add -A
git commit -m "feat: add image picker to location, item, monster, and stash edit popups"
```

---

## Part B: Shared Auth with Registrace

### Task 6: Add registrace user-check endpoint (registrace repo)

**Repo:** `C:\Users\TomášPajonk\source\repos\timurlain\registrace-ovcina-cz`

**Files:**
- Modify: `src/RegistraceOvcina.Web/Features/Integration/IntegrationApiEndpoints.cs`

**Step 1: Add the endpoint**

In the `MapIntegrationApiEndpoints` method, add:

```csharp
group.MapGet("/users/by-email", async (string email, ApplicationDbContext db) =>
{
    var user = await db.Users
        .Where(u => u.NormalizedEmail == email.ToUpperInvariant())
        .Select(u => new { u.DisplayName, u.IsActive })
        .FirstOrDefaultAsync();

    if (user is null || !user.IsActive)
        return Results.Ok(new { Exists = false });

    var appUser = await db.Users.FirstAsync(u => u.NormalizedEmail == email.ToUpperInvariant());
    var roles = await db.UserRoles
        .Where(ur => ur.UserId == appUser.Id)
        .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name)
        .ToListAsync();

    return Results.Ok(new { Exists = true, DisplayName = user.DisplayName, Roles = roles });
}).AllowAnonymous(); // Protected by API key filter on the group
```

**Step 2: Build and test**

```bash
cd C:\Users\TomášPajonk\source\repos\timurlain\registrace-ovcina-cz
dotnet build
```

**Step 3: Commit in registrace repo**

```bash
git add -A
git commit -m "feat: add /api/v1/users/by-email endpoint for OvčinaHra auth"
```

---

### Task 7: Add OAuth2 packages and RegistraceClient service

**Files:**
- Modify: `src/OvcinaHra.Api/OvcinaHra.Api.csproj`
- Create: `src/OvcinaHra.Api/Services/RegistraceClient.cs`

**Step 1: Add OAuth packages**

```bash
cd src/OvcinaHra.Api
dotnet add package Microsoft.AspNetCore.Authentication.Google
dotnet add package Microsoft.AspNetCore.Authentication.MicrosoftAccount
```

Note: Seznam OAuth is custom — we'll configure it manually via generic OAuth handler.

**Step 2: Create RegistraceClient**

```csharp
using System.Net.Http.Json;

namespace OvcinaHra.Api.Services;

public record RegistraceUserInfo(bool Exists, string? DisplayName, List<string>? Roles);

public class RegistraceClient
{
    private readonly HttpClient _http;

    public RegistraceClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<RegistraceUserInfo> CheckUserAsync(string email, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/v1/users/by-email?email={Uri.EscapeDataString(email)}", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RegistraceUserInfo>(ct) ?? new(false, null, null);
    }
}
```

**Step 3: Register HttpClient in Program.cs**

Add to service registration:

```csharp
builder.Services.AddHttpClient<RegistraceClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Registrace:BaseUrl"]!);
    client.DefaultRequestHeaders.Add("X-Api-Key", builder.Configuration["Registrace:ApiKey"]);
});
```

**Step 4: Add config to appsettings.Development.json**

```json
"Registrace": {
  "BaseUrl": "https://localhost:5001",
  "ApiKey": "dev-api-key"
}
```

**Step 5: Build**

```bash
dotnet build src/OvcinaHra.Api
```

**Step 6: Commit**

```bash
git add src/OvcinaHra.Api/OvcinaHra.Api.csproj src/OvcinaHra.Api/Services/RegistraceClient.cs src/OvcinaHra.Api/Program.cs src/OvcinaHra.Api/appsettings.Development.json
git commit -m "feat: add RegistraceClient and OAuth2 packages"
```

---

### Task 8: Server-side OAuth login and callback endpoints

**Files:**
- Modify: `src/OvcinaHra.Api/Endpoints/AuthEndpoints.cs`
- Modify: `src/OvcinaHra.Api/Program.cs`

**Step 1: Add OAuth provider configuration in Program.cs**

Replace the current authentication setup with:

```csharp
var authBuilder = builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!)),
        ClockSkew = TimeSpan.FromSeconds(30)
    };
})
.AddCookie("ExternalLogin", options =>
{
    options.Cookie.SameSite = SameSiteMode.Lax;
});

// Google
var googleId = builder.Configuration["ExternalAuth:Google:ClientId"];
var googleSecret = builder.Configuration["ExternalAuth:Google:ClientSecret"];
if (!string.IsNullOrEmpty(googleId) && !string.IsNullOrEmpty(googleSecret))
{
    authBuilder.AddGoogle(options =>
    {
        options.SignInScheme = "ExternalLogin";
        options.ClientId = googleId;
        options.ClientSecret = googleSecret;
    });
}

// Microsoft
var msId = builder.Configuration["ExternalAuth:Microsoft:ClientId"];
var msSecret = builder.Configuration["ExternalAuth:Microsoft:ClientSecret"];
if (!string.IsNullOrEmpty(msId) && !string.IsNullOrEmpty(msSecret))
{
    authBuilder.AddMicrosoftAccount(options =>
    {
        options.SignInScheme = "ExternalLogin";
        options.ClientId = msId;
        options.ClientSecret = msSecret;
    });
}
```

**Step 2: Add login/callback endpoints to AuthEndpoints.cs**

Add these endpoints inside `MapAuthEndpoints`:

```csharp
// OAuth login — redirects to external provider
group.MapGet("/login/{provider}", (string provider, HttpContext context) =>
{
    var redirectUrl = "/api/auth/callback";
    var properties = new AuthenticationProperties { RedirectUri = redirectUrl };
    return Results.Challenge(properties, [provider]);
}).AllowAnonymous();

// OAuth callback — verifies with registrace, issues JWT, redirects to client
group.MapGet("/callback", async (
    HttpContext context, RegistraceClient registrace, IConfiguration config) =>
{
    var result = await context.AuthenticateAsync("ExternalLogin");
    if (!result.Succeeded)
        return Results.Redirect("/login?error=auth_failed");

    var email = result.Principal?.FindFirstValue(ClaimTypes.Email);
    var name = result.Principal?.FindFirstValue(ClaimTypes.Name);

    if (string.IsNullOrEmpty(email))
        return Results.Redirect("/login?error=no_email");

    // Verify with registrace
    var userInfo = await registrace.CheckUserAsync(email);
    if (!userInfo.Exists)
        return Results.Redirect("/login?error=not_registered");

    // Issue JWT
    var displayName = userInfo.DisplayName ?? name ?? email;
    var token = GenerateToken(config, [
        new(ClaimTypes.NameIdentifier, email),
        new(ClaimTypes.Email, email),
        new(ClaimTypes.Name, displayName),
        new(ClaimTypes.Role, "Organizer")
    ]);

    // Sign out the external cookie
    await context.SignOutAsync("ExternalLogin");

    // Redirect to WASM client with token in fragment
    var clientUrl = config.GetSection("Cors:Origins").Get<string[]>()?.FirstOrDefault()
        ?? "https://localhost:5290";
    return Results.Redirect($"{clientUrl}/auth-callback#token={token.Token}&expires={token.ExpiresInSeconds}");
}).AllowAnonymous();
```

Add required usings:
```csharp
using Microsoft.AspNetCore.Authentication;
using OvcinaHra.Api.Services;
```

**Step 3: Build**

```bash
dotnet build src/OvcinaHra.Api
```

**Step 4: Commit**

```bash
git add src/OvcinaHra.Api/Endpoints/AuthEndpoints.cs src/OvcinaHra.Api/Program.cs
git commit -m "feat: add OAuth login/callback endpoints with registrace verification"
```

---

### Task 9: WASM client auth callback and login page

**Files:**
- Create: `src/OvcinaHra.Client/Pages/AuthCallback.razor`
- Modify: `src/OvcinaHra.Client/Pages/Login.razor`

**Step 1: Create auth callback page**

```razor
@page "/auth-callback"
@inject IJSRuntime JS
@inject NavigationManager Nav

<p>Přihlašování...</p>

@code {
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        // Extract token from URL fragment via JS
        var token = await JS.InvokeAsync<string>("eval",
            "new URLSearchParams(window.location.hash.substring(1)).get('token')");

        if (!string.IsNullOrEmpty(token))
        {
            await JS.InvokeVoidAsync("localStorage.setItem", "auth_token", token);
            Nav.NavigateTo("/", forceLoad: true);
        }
        else
        {
            Nav.NavigateTo("/login?error=no_token");
        }
    }
}
```

**Step 2: Update Login.razor to show provider buttons**

Add provider login buttons alongside the existing dev-token form:

```razor
<div class="mb-4">
    <h5>Přihlásit se</h5>
    <div class="d-flex flex-column gap-2" style="max-width: 300px;">
        <a href="@(ApiBaseUrl)/api/auth/login/Google" class="btn btn-outline-dark">
            Přihlásit přes Google
        </a>
        <a href="@(ApiBaseUrl)/api/auth/login/Microsoft" class="btn btn-outline-primary">
            Přihlásit přes Microsoft
        </a>
    </div>
</div>
```

Where `ApiBaseUrl` is read from config (`IConfiguration["ApiBaseUrl"]`).

**Step 3: Build**

```bash
dotnet build src/OvcinaHra.Client
```

**Step 4: Commit**

```bash
git add src/OvcinaHra.Client/Pages/AuthCallback.razor src/OvcinaHra.Client/Pages/Login.razor
git commit -m "feat: add OAuth callback page and provider login buttons"
```

---

### Task 10: Integration test for auth flow

**Files:**
- Modify: `tests/OvcinaHra.Api.Tests/Endpoints/AuthEndpointTests.cs`

**Step 1: Add test for dev-token still works**

The existing auth tests should still pass. Add a test verifying the callback rejects unknown emails:

```csharp
[Fact]
public async Task Callback_WithoutExternalAuth_ReturnsRedirect()
{
    // Direct GET to callback without external auth session should redirect to login
    var response = await Client.GetAsync("/api/auth/callback");
    // Should redirect to login with error (no external auth result)
    Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    Assert.Contains("error", response.Headers.Location?.ToString() ?? "");
}
```

**Step 2: Run all auth tests**

```bash
dotnet test tests/OvcinaHra.Api.Tests --filter "AuthEndpointTests"
```

**Step 3: Commit**

```bash
git add tests/OvcinaHra.Api.Tests/Endpoints/AuthEndpointTests.cs
git commit -m "test: add auth callback integration test"
```

---

## Summary

| Task | Feature | Description |
|---|---|---|
| 1 | Images | Azure.Storage.Blobs package + Azurite docker |
| 2 | Images | BlobStorageService (upload, SAS URL, delete) |
| 3 | Images | Image upload/get/delete endpoints + tests |
| 4 | Images | Reusable ImagePicker Blazor component |
| 5 | Images | Add ImagePicker to all entity edit popups |
| 6 | Auth | Registrace `/api/v1/users/by-email` endpoint |
| 7 | Auth | OAuth packages + RegistraceClient service |
| 8 | Auth | Server-side login/callback endpoints |
| 9 | Auth | WASM auth callback page + login buttons |
| 10 | Auth | Integration test for auth flow |

**Dependencies:** Tasks 1→2→3→4→5 (sequential). Tasks 6→7→8→9→10 (sequential). The two chains are independent and can run in parallel.
