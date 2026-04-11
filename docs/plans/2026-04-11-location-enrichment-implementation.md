# Location Enrichment Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Enrich Location entity with Details, GamePotential, Region fields; remap IDs from MD file indexes; update UI with tabbed popup.

**Architecture:** Add 3 nullable columns via EF migration. Python script parses 85 MD files, shifts existing IDs to 101+, inserts with explicit IDs 1-88. UI gets wider popup with Základní/Lore tabs.

**Tech Stack:** .NET 10, EF Core, PostgreSQL, Blazor WASM, DevExpress DxGrid/DxPopup/DxTabs, Python 3 (import script)

**Design doc:** `docs/plans/2026-04-11-location-enrichment-design.md`

---

### Task 1: Add new fields to Location entity

**Files:**
- Modify: `src/OvcinaHra.Shared/Domain/Entities/Location.cs`

**Step 1: Add 3 new properties after SetupNotes (line 14)**

After `public string? SetupNotes { get; set; }` add:

```csharp
public string? Details { get; set; }
public string? GamePotential { get; set; }
public string? Region { get; set; }
```

**Step 2: Verify build**

Run: `dotnet build src/OvcinaHra.Shared/OvcinaHra.Shared.csproj --configfile nuget.config`
Expected: Build succeeded, 0 errors

**Step 3: Commit**

```
git add src/OvcinaHra.Shared/Domain/Entities/Location.cs
git commit -m "feat: add Details, GamePotential, Region fields to Location entity [v0.2.11]"
```

---

### Task 2: Update EF Configuration and create migration

**Files:**
- Modify: `src/OvcinaHra.Api/Data/Configurations/LocationConfiguration.cs`

**Step 1: Add column configuration**

After the existing `SetupNotes` line (no max length needed — these are long text fields), update the tsvector computed column to include `Details` and `Region`.

The existing tsvector line looks like:
```csharp
builder.Property("SearchVector")
    .HasColumnType("tsvector")
    .HasComputedColumnSql(
        "to_tsvector('simple', coalesce(\"Name\",'') || ' ' || coalesce(\"Description\",'') || ' ' || coalesce(\"NpcInfo\",'') || ' ' || coalesce(\"SetupNotes\",''))",
        stored: true);
```

Update it to:
```csharp
builder.Property("SearchVector")
    .HasColumnType("tsvector")
    .HasComputedColumnSql(
        "to_tsvector('simple', coalesce(\"Name\",'') || ' ' || coalesce(\"Description\",'') || ' ' || coalesce(\"Details\",'') || ' ' || coalesce(\"Region\",'') || ' ' || coalesce(\"NpcInfo\",'') || ' ' || coalesce(\"SetupNotes\",''))",
        stored: true);
```

**Step 2: Create EF migration**

Run: `cd src/OvcinaHra.Api && dotnet ef migrations add AddLocationLoreFields`

**Step 3: Verify build**

Run: `dotnet build --configfile nuget.config`
Expected: Build succeeded

**Step 4: Commit**

```
git add src/OvcinaHra.Api/Data/Configurations/LocationConfiguration.cs
git add src/OvcinaHra.Api/Migrations/
git commit -m "feat: EF migration for Location Details, GamePotential, Region columns [v0.2.11]"
```

---

### Task 3: Update DTOs

**Files:**
- Modify: `src/OvcinaHra.Shared/Dtos/LocationDtos.cs`

**Step 1: Update all 4 DTOs**

```csharp
public record LocationListDto(
    int Id,
    string Name,
    LocationKind LocationKind,
    string? Region,
    decimal? Latitude,
    decimal? Longitude,
    int? ParentLocationId);

public record LocationDetailDto(
    int Id,
    string Name,
    string? Description,
    string? Details,
    string? GamePotential,
    string? Region,
    LocationKind LocationKind,
    decimal? Latitude,
    decimal? Longitude,
    string? ImagePath,
    string? PlacementPhotoPath,
    string? NpcInfo,
    string? SetupNotes,
    int? ParentLocationId,
    List<LocationVariantDto> Variants);

public record LocationVariantDto(int Id, string Name, LocationKind LocationKind);

public record CreateLocationDto(
    string Name,
    LocationKind LocationKind,
    decimal? Latitude = null,
    decimal? Longitude = null,
    string? Description = null,
    string? Details = null,
    string? GamePotential = null,
    string? Region = null,
    string? NpcInfo = null,
    string? SetupNotes = null,
    int? ParentLocationId = null);

public record UpdateLocationDto(
    string Name,
    LocationKind LocationKind,
    decimal? Latitude = null,
    decimal? Longitude = null,
    string? Description = null,
    string? Details = null,
    string? GamePotential = null,
    string? Region = null,
    string? NpcInfo = null,
    string? SetupNotes = null,
    int? ParentLocationId = null);
```

**Step 2: Fix compile errors in LocationEndpoints.cs**

The endpoint mapping code creates DTOs from entity — add the new fields to the `Select` projections:

In `GetAll` handler, update the `LocationListDto` projection to include `Region = l.Region`.

In `GetById` handler, update the `LocationDetailDto` construction to include `Details = l.Details, GamePotential = l.GamePotential, Region = l.Region`.

In `Create` handler, map `dto.Details`, `dto.GamePotential`, `dto.Region` to the entity.

In `Update` handler, map the same 3 fields.

**Step 3: Fix compile errors in tests**

Update `LocationEndpointTests.cs` — the `UpdateLocationDto` constructor calls need the new optional parameters. Since they have defaults, existing calls should compile, but verify.

**Step 4: Verify build + tests**

Run: `dotnet build --configfile nuget.config && dotnet test tests/OvcinaHra.Api.Tests`
Expected: Build succeeded, all tests pass

**Step 5: Commit**

```
git add src/OvcinaHra.Shared/Dtos/LocationDtos.cs
git add src/OvcinaHra.Api/Endpoints/LocationEndpoints.cs
git add tests/OvcinaHra.Api.Tests/Endpoints/LocationEndpointTests.cs
git commit -m "feat: add Details, GamePotential, Region to Location DTOs and endpoints [v0.2.11]"
```

---

### Task 4: Write integration tests for new fields

**Files:**
- Modify: `tests/OvcinaHra.Api.Tests/Endpoints/LocationEndpointTests.cs`

**Step 1: Write test for new fields round-trip**

```csharp
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
```

**Step 2: Run tests**

Run: `dotnet test tests/OvcinaHra.Api.Tests --filter "LocationEndpointTests"`
Expected: All tests pass

**Step 3: Commit**

```
git add tests/OvcinaHra.Api.Tests/Endpoints/LocationEndpointTests.cs
git commit -m "test: integration tests for Location lore fields [v0.2.11]"
```

---

### Task 5: Update LocationList UI — wider popup with tabs

**Files:**
- Modify: `src/OvcinaHra.Client/Pages/Locations/LocationList.razor`

**Step 1: Widen the detail popup**

Change the DxPopup width to `Width="900px"`.

**Step 2: Add tab layout inside the popup body**

Replace the existing `DxFormLayout` with a `DxTabs` containing two tabs:

Tab 1 "Základní": existing fields — Name, LocationKind, Region (new DxTextBox), Latitude, Longitude, ImagePath, PlacementPhotoPath, NpcInfo (DxMemo), SetupNotes (DxMemo), ParentLocationId.

Tab 2 "Lore": Description (DxMemo, ReadOnly by default), Details (DxMemo, ReadOnly by default), GamePotential (DxMemo, always editable). Plus a "Upravit texty" button that sets `isLoreEditable = true`.

**Step 3: Add component state**

Add to `@code` block:
```csharp
private bool isLoreEditable;
```

Reset `isLoreEditable = false` when opening the popup (in the row click handler).

**Step 4: Bind new fields**

Add `editDetails`, `editGamePotential`, `editRegion` fields to the component state (same pattern as existing `editDescription`, `editNpcInfo`, etc.). Populate them from the detail DTO on row click. Send them in Create/Update API calls.

**Step 5: Add Region column to grid**

Add a `DxGridDataColumn` for Region after the LocationKind column.

**Step 6: Verify build**

Run: `dotnet build src/OvcinaHra.Client/OvcinaHra.Client.csproj --configfile nuget.config`
Expected: Build succeeded

**Step 7: Commit**

```
git add src/OvcinaHra.Client/Pages/Locations/LocationList.razor
git commit -m "feat: LocationList tabbed popup with Lore tab, Region column [v0.2.11]"
```

---

### Task 6: Python import script — parse MD files

**Files:**
- Create: `scripts/import_locations.py`

**Step 1: Write MD parser**

Python script that:
1. Reads all `*.md` files from the lokace directory (excluding `index.md`)
2. Parses each file with regex:
   - Index: from filename (e.g. `1-aradhrynd.md` → 1, `m1-vychodni-brana.md` → 82)
   - Name: from `### N. Name` line
   - Typ + Oblast: from `**Typ:** ... | **Oblast:** ...` line
   - Popis: from `**Popis:** ...` line
   - Podrobnosti: from `**Podrobnosti:** ...` line
   - Herní potenciál: from `**Herní potenciál:** ...` line
   - MJ prompt: from fenced code block after `**Midjourney prompt:**`
3. Maps Typ to LocationKind enum string
4. Outputs parsed data as JSON for verification

**Step 2: Test parser standalone**

Run: `python scripts/import_locations.py --parse-only`
Expected: Prints JSON with all 85 locations parsed correctly

**Step 3: Commit**

```
git add scripts/import_locations.py
git commit -m "feat: MD location parser script [v0.2.11]"
```

---

### Task 7: Python import script — DB migration

**Files:**
- Modify: `scripts/import_locations.py`

**Step 1: Add DB operations**

Extend the script with `--migrate` mode that:
1. Connects to target DB (connection string from CLI arg or env var)
2. In a transaction:
   - `SET session_replication_role = 'replica'` (disable FK triggers)
   - `UPDATE "Locations" SET "Id" = "Id" + 100`
   - Update all FK tables: `"GameLocations"."LocationId"`, `"SecretStashes"."LocationId"`, `"Buildings"."LocationId"`, `"QuestLocationLinks"."LocationId"`, `"TreasureQuests"."LocationId"`, `"Locations"."ParentLocationId"` — all `+100`
   - `SET session_replication_role = 'origin'`
3. For each parsed MD location:
   - Search existing locations (now at 101+) by exact name match
   - If found: merge GPS, ImagePath, PlacementPhotoPath, NpcInfo from old record
   - If fuzzy match (Levenshtein distance ≤ 3): print to console, ask user Y/N
   - Insert new location with explicit ID
   - If merged from old record: delete the old 101+ record
4. Reset PG sequence: `SELECT setval(pg_get_serial_sequence('"Locations"', 'Id'), (SELECT MAX("Id") FROM "Locations"))`
5. Print summary: matched, fuzzy-matched, new, leftover

**Step 2: Test against local DB first**

Run: `python scripts/import_locations.py --migrate --connection "Host=localhost;Port=5434;Database=ovcinahra;Username=ovcinahra;Password=ovcinahra"`

**Step 3: Run against production**

Run: `python scripts/import_locations.py --migrate --connection "Host=ovcina-db.postgres.database.azure.com;Port=5432;Database=ovcinahra;Username=ovcina_admin;Password=vsechnoJeTuBozi30;SSL Mode=Require"`

**Step 4: Commit**

```
git add scripts/import_locations.py
git commit -m "feat: location import script with ID remapping and name matching [v0.2.11]"
```

---

### Task 8: Deploy and verify

**Step 1: Push to trigger auto-deploy**

Run: `git push origin main`

**Step 2: Wait for GitHub Actions deploy**

Check: `gh run list --limit 1`

**Step 3: Run migration against production**

The EF migration runs automatically on app startup (auto-migrate is enabled).

**Step 4: Run import script against production**

Run: `python scripts/import_locations.py --migrate --connection "Host=ovcina-db.postgres.database.azure.com;..."`

**Step 5: Verify production**

Check `https://hra.ovcina.cz` — locations should show with IDs 1-81, Region column visible, Lore tab with Description/Details.

**Step 6: Commit any fixes**
