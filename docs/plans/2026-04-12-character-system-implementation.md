# Character System Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add character management + QR scan workflow so organizers can track player characters at LARP events.

**Architecture:** Three new entities (Character, CharacterAssignment, CharacterEvent) with event-sourced progression. Catalog CRUD for characters, per-game assignments, event log per assignment. Scan page `/p/{personId}` is the QR target — mobile-first, quick actions for field use. Import from registrace seed API for initial character population.

**Tech Stack:** EF Core (Npgsql), Minimal API, Blazor WASM, existing PlayerClass enum, existing JWT auth (ClaimTypes.NameIdentifier + ClaimTypes.Name for organizer identity).

**Design doc:** `prompt-hra-character-system.md` (OneDrive secret KB)

**Version:** Bump `src/OvcinaHra.Client/OvcinaHra.Client.csproj` → 0.3.0 (major feature)

---

## Phase 1: Entities + Migrations (Tasks 1-3)

### Task 1: Create Character entity + EF config

**Files:**
- Create: `src/OvcinaHra.Shared/Domain/Entities/Character.cs`
- Create: `src/OvcinaHra.Api/Data/Configurations/CharacterConfiguration.cs`
- Modify: `src/OvcinaHra.Api/Data/WorldDbContext.cs`

**Step 1: Create Character entity**

```csharp
// src/OvcinaHra.Shared/Domain/Entities/Character.cs
using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Shared.Domain.Entities;

public class Character
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Race { get; set; }
    public PlayerClass? Class { get; set; }
    public string? Kingdom { get; set; }
    public int? BirthYear { get; set; }
    public string? Notes { get; set; }
    public bool IsPlayedCharacter { get; set; }
    public int? ExternalPersonId { get; set; }
    public int? ParentCharacterId { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public Character? ParentCharacter { get; set; }
    public ICollection<Character> Children { get; set; } = [];
    public ICollection<CharacterAssignment> Assignments { get; set; } = [];
}
```

**Step 2: Create EF configuration**

```csharp
// src/OvcinaHra.Api/Data/Configurations/CharacterConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OvcinaHra.Shared.Domain.Entities;

namespace OvcinaHra.Api.Data.Configurations;

public class CharacterConfiguration : IEntityTypeConfiguration<Character>
{
    public void Configure(EntityTypeBuilder<Character> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Class).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(e => e.ExternalPersonId).IsUnique().HasFilter("\"ExternalPersonId\" IS NOT NULL");
        builder.HasOne(e => e.ParentCharacter).WithMany(e => e.Children).HasForeignKey(e => e.ParentCharacterId);
        builder.HasQueryFilter(e => !e.IsDeleted);
    }
}
```

**Step 3: Add DbSet to WorldDbContext**

Add `public DbSet<Character> Characters => Set<Character>();`

**Step 4: Build to verify compilation**

Run: `dotnet build --nologo -v q`

**Step 5: Commit**

```
feat: add Character entity with EF config [v0.3.0]
```

### Task 2: Create CharacterAssignment + CharacterEvent entities

**Files:**
- Create: `src/OvcinaHra.Shared/Domain/Entities/CharacterAssignment.cs`
- Create: `src/OvcinaHra.Shared/Domain/Entities/CharacterEvent.cs`
- Create: `src/OvcinaHra.Shared/Domain/Enums/CharacterEventType.cs`
- Modify: `src/OvcinaHra.Api/Data/Configurations/CharacterConfiguration.cs` (add configs)
- Modify: `src/OvcinaHra.Api/Data/WorldDbContext.cs`

**Step 1: Create CharacterEventType enum**

```csharp
// src/OvcinaHra.Shared/Domain/Enums/CharacterEventType.cs
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace OvcinaHra.Shared.Domain.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CharacterEventType
{
    [Display(Name = "Zvýšení úrovně")]
    LevelUp,

    [Display(Name = "Získání dovednosti")]
    SkillGained,

    [Display(Name = "Změna bodů")]
    PointsChanged,

    [Display(Name = "Poznámka")]
    Note,

    [Display(Name = "Volba povolání")]
    ClassChosen
}
```

**Step 2: Create CharacterAssignment entity**

```csharp
// src/OvcinaHra.Shared/Domain/Entities/CharacterAssignment.cs
namespace OvcinaHra.Shared.Domain.Entities;

public class CharacterAssignment
{
    public int Id { get; set; }
    public int CharacterId { get; set; }
    public int GameId { get; set; }
    public int ExternalPersonId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? EndedAtUtc { get; set; }

    public Character Character { get; set; } = null!;
    public ICollection<CharacterEvent> Events { get; set; } = [];
}
```

Note: `GameId` is an external reference to registrace Game.Id — NOT a local FK to the Game table.

**Step 3: Create CharacterEvent entity**

```csharp
// src/OvcinaHra.Shared/Domain/Entities/CharacterEvent.cs
using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Shared.Domain.Entities;

public class CharacterEvent
{
    public int Id { get; set; }
    public int CharacterAssignmentId { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public required string OrganizerUserId { get; set; }
    public required string OrganizerName { get; set; }
    public CharacterEventType EventType { get; set; }
    public required string Data { get; set; }
    public string? Location { get; set; }

    public CharacterAssignment Assignment { get; set; } = null!;
}
```

**Step 4: Add EF configs for Assignment and Event**

Append to `CharacterConfiguration.cs`:

```csharp
public class CharacterAssignmentConfiguration : IEntityTypeConfiguration<CharacterAssignment>
{
    public void Configure(EntityTypeBuilder<CharacterAssignment> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasOne(e => e.Character).WithMany(c => c.Assignments).HasForeignKey(e => e.CharacterId);
        builder.HasIndex(e => e.GameId);
        builder.HasIndex(e => e.ExternalPersonId);
        builder.HasIndex(e => e.CharacterId);
    }
}

public class CharacterEventConfiguration : IEntityTypeConfiguration<CharacterEvent>
{
    public void Configure(EntityTypeBuilder<CharacterEvent> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.EventType).HasConversion<string>().HasMaxLength(30);
        builder.HasOne(e => e.Assignment).WithMany(a => a.Events).HasForeignKey(e => e.CharacterAssignmentId);
        builder.HasIndex(e => e.CharacterAssignmentId);
    }
}
```

**Step 5: Add DbSets to WorldDbContext**

```csharp
public DbSet<CharacterAssignment> CharacterAssignments => Set<CharacterAssignment>();
public DbSet<CharacterEvent> CharacterEvents => Set<CharacterEvent>();
```

**Step 6: Build**

**Step 7: Commit**

```
feat: add CharacterAssignment + CharacterEvent entities [v0.3.0]
```

### Task 3: Create EF migration

**Step 1: Generate migration**

Run: `dotnet ef migrations add AddCharacterSystem --project src/OvcinaHra.Api --startup-project src/OvcinaHra.Api`

**Step 2: Review migration file** — verify it creates Characters, CharacterAssignments, CharacterEvents tables with correct columns, indexes, and constraints.

**Step 3: Run tests** to ensure migration applies cleanly on Testcontainers DB.

**Step 4: Commit**

```
chore: add AddCharacterSystem migration [v0.3.0]
```

---

## Phase 2: Character CRUD API + DTOs (Tasks 4-5)

### Task 4: Create Character DTOs

**Files:**
- Create: `src/OvcinaHra.Shared/Dtos/CharacterDtos.cs`

```csharp
using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Shared.Dtos;

public record CharacterListDto(
    int Id, string Name, string? Race, PlayerClass? Class,
    string? Kingdom, bool IsPlayedCharacter, int? ExternalPersonId);

public record CharacterDetailDto(
    int Id, string Name, string? Race, PlayerClass? Class,
    string? Kingdom, int? BirthYear, string? Notes,
    bool IsPlayedCharacter, int? ExternalPersonId,
    int? ParentCharacterId, string? ParentCharacterName,
    string? ImagePath, DateTime CreatedAtUtc, DateTime UpdatedAtUtc);

public record CreateCharacterDto(
    string Name,
    string? Race = null,
    PlayerClass? Class = null,
    string? Kingdom = null,
    int? BirthYear = null,
    string? Notes = null,
    bool IsPlayedCharacter = false,
    int? ExternalPersonId = null,
    int? ParentCharacterId = null);

public record UpdateCharacterDto(
    string Name,
    string? Race,
    PlayerClass? Class,
    string? Kingdom,
    int? BirthYear,
    string? Notes,
    bool IsPlayedCharacter,
    int? ExternalPersonId,
    int? ParentCharacterId);

// Per-game assignment
public record CharacterAssignmentDto(
    int Id, int CharacterId, string CharacterName,
    int GameId, int ExternalPersonId,
    bool IsActive, DateTime StartedAtUtc, DateTime? EndedAtUtc);

// Event log
public record CharacterEventDto(
    int Id, CharacterEventType EventType, string Data,
    string? Location, string OrganizerName, DateTime Timestamp);

public record CreateCharacterEventDto(
    CharacterEventType EventType, string Data, string? Location = null);

// Scan page composite response
public record ScanCharacterDto(
    int CharacterId, string Name, string? Race, PlayerClass? Class,
    string? Kingdom, int CurrentLevel, int TotalXp,
    List<string> Skills, List<CharacterEventDto> RecentEvents);

// Import summary
public record ImportResultDto(int Created, int Updated, int Skipped);
```

**Step 1: Create the file**

**Step 2: Build**

**Step 3: Commit**

```
feat: add Character DTOs [v0.3.0]
```

### Task 5: Create Character CRUD endpoints + tests

**Files:**
- Create: `src/OvcinaHra.Api/Endpoints/CharacterEndpoints.cs`
- Modify: `src/OvcinaHra.Api/Program.cs` — register `MapCharacterEndpoints()`
- Create: `tests/OvcinaHra.Api.Tests/Endpoints/CharacterEndpointTests.cs`

**Step 1: Write integration tests** for GET /, GET /{id}, POST /, PUT /{id}, DELETE /{id}

Test cases:
- `GetAll_Empty_ReturnsEmptyList`
- `Create_ValidCharacter_ReturnsCreated`
- `GetById_ExistingCharacter_ReturnsCharacter`
- `GetById_DeletedCharacter_ReturnsNotFound` (soft delete via query filter)
- `Update_ExistingCharacter_ReturnsNoContent`
- `Delete_ExistingCharacter_SoftDeletes`
- `GetAll_WithSearchQuery_FiltersResults`

**Step 2: Run tests to verify they fail**

**Step 3: Implement CharacterEndpoints.cs** following the existing pattern (MapGroup, TypedResults, WorldDbContext).

Endpoints:
```
GET    /api/characters?search=...  — list all (non-deleted), optional name search
GET    /api/characters/{id}        — detail with parent name
POST   /api/characters             — create
PUT    /api/characters/{id}        — update (sets UpdatedAtUtc)
DELETE /api/characters/{id}        — soft delete (IsDeleted = true)
GET    /api/characters/{id}/assignments — game appearances
```

**Step 4: Register in Program.cs** — add `app.MapGroup(...).MapCharacterEndpoints()` next to existing endpoint registrations.

**Step 5: Run tests to verify they pass**

**Step 6: Commit**

```
feat: Character CRUD endpoints with tests [v0.3.0]
```

---

## Phase 3: Import from Registrace (Task 6)

### Task 6: Import endpoint + service

**Files:**
- Create: `src/OvcinaHra.Api/Services/CharacterImportService.cs`
- Modify: `src/OvcinaHra.Api/Endpoints/CharacterEndpoints.cs`
- Modify: `src/OvcinaHra.Api/Program.cs` — register HttpClient + config

**Step 1: Add configuration**

In `appsettings.json` / environment:
```json
"IntegrationApi": {
    "BaseUrl": "https://registrace.ovcina.cz",
    "ApiKey": "ab5380ce6b84a05b1085cd2550c162b0a38a15c645a0e092c00069b5976237d0"
}
```

**Step 2: Create CharacterImportService**

- Calls `GET {BaseUrl}/api/v1/games/{gameId}/characters` with `X-Api-Key` header
- For each character seed:
  - Match by ExternalPersonId → update if exists, create if not
  - Create CharacterAssignment for the game
- Return ImportResultDto(created, updated, skipped)

**Step 3: Add endpoint**

```
POST /api/characters/import/{gameId}  — trigger import
```

**Step 4: Register IHttpClientFactory for the import service in Program.cs**

**Step 5: Write test** — mock the external API call or test the endpoint with a stub. At minimum test the upsert logic with seeded DB data.

**Step 6: Commit**

```
feat: character import from registrace [v0.3.0]
```

---

## Phase 4: Scan Page + Event Log (Tasks 7-9)

### Task 7: Scan API endpoints

**Files:**
- Create: `src/OvcinaHra.Api/Endpoints/ScanEndpoints.cs`
- Modify: `src/OvcinaHra.Api/Program.cs`
- Create: `tests/OvcinaHra.Api.Tests/Endpoints/ScanEndpointTests.cs`

**Endpoints:**
```
GET  /api/scan/{personId}        — resolve → ScanCharacterDto
POST /api/scan/{personId}/events — log event (auth required, reads organizer from claims)
GET  /api/scan/{personId}/events — recent events (last 20)
```

**Scan resolution logic:**
1. Find active CharacterAssignment where ExternalPersonId = personId and IsActive = true
2. Load Character + all events for this assignment
3. Compute: currentLevel = count of LevelUp events, totalXp = count of LevelUp events (1 XP per level), skills = SkillGained event Data values
4. Return ScanCharacterDto

**Event creation logic:**
1. Read organizer identity from `ClaimTypes.NameIdentifier` (userId) and `ClaimTypes.Name` (display name)
2. If EventType = ClassChosen: also update Character.Class from event Data (parse PlayerClass)
3. If EventType = LevelUp: auto-set Data to `{"level": N}` where N = current level + 1
4. Create CharacterEvent, save

**Test cases:**
- `Scan_ActiveCharacter_ReturnsProfile`
- `Scan_NoActiveAssignment_ReturnsNotFound`
- `PostEvent_LevelUp_IncrementsLevel`
- `PostEvent_ClassChosen_SetsCharacterClass`
- `PostEvent_PointsChanged_RecordsEvent`

**Step 1: Write tests → Step 2: Implement → Step 3: Commit**

```
feat: scan API endpoints with event logging [v0.3.0]
```

### Task 8: Character list + edit pages (Blazor)

**Files:**
- Create: `src/OvcinaHra.Client/Pages/Characters/CharacterList.razor`

**Pattern:** Same as MonsterList — OvcinaGrid + DxPopup edit modal. No catalog toggle needed (characters are global).

Grid columns: Name, Race, Class (GetDisplayName), Kingdom, IsPlayedCharacter badge
Edit form: All fields from CreateCharacterDto, ParentCharacter dropdown (ComboBox searching characters)

Empty state: "Žádné postavy. Importujte z registrace nebo vytvořte novou."

**Import button** on the page header — calls POST /api/characters/import/{gameId}, shows result toast.

**Step 1: Create page → Step 2: Build → Step 3: Commit**

```
feat: character list + edit page [v0.3.0]
```

### Task 9: Scan page (QR target — mobile-first)

**Files:**
- Create: `src/OvcinaHra.Client/Pages/Scan/ScanPage.razor`

**Route:** `/p/{PersonId:int}`

**This is the most critical page — used on phones in the field.**

**Layout:**
1. **Header:** Character name (large), Kingdom badge, Race
2. **Status bar:** Level badge, Class badge (or "Začátečník"), XP counter
3. **Class picker:** Only shown when totalXp >= 5 AND class is null. Four big buttons (PlayerClass enum values with GetDisplayName). Tapping sets the class.
4. **Quick action buttons** (large, mobile-friendly, stacked vertically):
   - "Level Up" (green) — one tap, immediate
   - "Přidat body" — opens modal: category picker (Dobré/Špatné/Neutrální) + amount + note
   - "Přidat poznámku" — opens modal: text area
   - "Přidat dovednost" — opens modal: skill name text
5. **Optional location field** — text input above actions, prepopulated from last event
6. **Recent events** — last 10, compact list (time, type icon, summary)

**If person not found:** Show error state: "Postava nenalezena. Tento hráč nemá aktivní postavu v aktuální hře."

**CSS priorities:** Large touch targets (min 48px), readable on small screens, minimal scrolling to reach actions.

**Step 1: Create page → Step 2: Build → Step 3: Test on mobile viewport → Step 4: Commit**

```
feat: scan page with quick actions [v0.3.0]
```

---

## Phase 5: Navigation + Polish (Task 10)

### Task 10: Wire up navigation + version bump

**Files:**
- Modify: `src/OvcinaHra.Client/Shared/NavMenu.razor` (or equivalent) — add "Postavy" nav item
- Modify: `src/OvcinaHra.Client/OvcinaHra.Client.csproj` — bump Version to 0.3.0

**Step 1: Add nav item with person icon (bi-person) for Characters page**

**Step 2: Verify scan page works without nav (it's a QR target, not a menu item)**

**Step 3: Run all tests**

**Step 4: Bump version**

**Step 5: Final commit**

```
feat: character nav + version bump to v0.3.0
```

---

## Key Implementation Notes

- **PlayerClass enum** already exists at `src/OvcinaHra.Shared/Domain/Enums/PlayerClass.cs` with Display names: Válečník, Střelec, Zloděj, Mág
- **Auth claims:** `ClaimTypes.NameIdentifier` = user ID, `ClaimTypes.Name` = display name (from AuthEndpoints.cs)
- **Soft delete:** Character uses `HasQueryFilter(e => !e.IsDeleted)` — EF automatically excludes deleted records from queries
- **GameId on CharacterAssignment** is an external reference to registrace, NOT a local FK to the Game table
- **CharacterEvent.Data** is freeform JSON — each EventType has its own shape:
  - LevelUp: `{"level": 3}`
  - PointsChanged: `{"category": "Dobré", "amount": 2, "note": "pomoc vesničanům"}`
  - SkillGained: `{"skill": "Léčení"}`
  - Note: `{"text": "Zajímavý rozhovor s kupcem"}`
  - ClassChosen: `{"class": "Warrior"}`
- **No offline sync** — retry on failure is sufficient
- **Test command:** `dotnet test tests/OvcinaHra.Api.Tests --nologo -v q`
- **Build command:** `dotnet build --nologo -v q`
