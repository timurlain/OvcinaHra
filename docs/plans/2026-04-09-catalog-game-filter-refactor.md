# Catalog/Game Filter Refactor — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Split the app into "active game" (filtered) and "world catalog" (all) views, so organizers work within a specific game edition by default and can browse/assign from the full catalog when needed.

**Architecture:** Each list page gets a `catalog` query parameter toggle. Game-filtered view is the default. Catalog view shows all entities with assign/unassign actions. Building entity moves from direct GameId FK to a GameBuilding join table (like GameLocation). Quest catalog shows cross-game quests with a copy-to-current-game action.

**Tech Stack:** ASP.NET Core Minimal API, EF Core + PostgreSQL, Blazor WASM, DevExpress Blazor (DxGrid, DxPopup, DxComboBox)

---

## Phase 1: Building Model Change (GameBuilding join table)

### Task 1.1: Create GameBuilding entity and update Building

**Files:**
- Create: `src/OvcinaHra.Shared/Domain/Entities/GameBuilding.cs`
- Modify: `src/OvcinaHra.Shared/Domain/Entities/Building.cs` — remove `GameId`, `Game` nav property; add `GameBuildings` collection
- Modify: `src/OvcinaHra.Shared/Domain/Entities/Game.cs` — change `Buildings` to `GameBuildings`

**Step 1: Create GameBuilding entity**

```csharp
// src/OvcinaHra.Shared/Domain/Entities/GameBuilding.cs
namespace OvcinaHra.Shared.Domain.Entities;

public class GameBuilding
{
    public int GameId { get; set; }
    public int BuildingId { get; set; }

    public Game Game { get; set; } = null!;
    public Building Building { get; set; } = null!;
}
```

**Step 2: Update Building entity**

Remove `GameId` and `Game` properties. Add `GameBuildings` collection:
```csharp
public class Building
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? ImagePath { get; set; }
    public int? LocationId { get; set; }
    public bool IsPrebuilt { get; set; }

    public Location? Location { get; set; }
    public ICollection<GameBuilding> GameBuildings { get; set; } = [];
    public ICollection<CraftingBuildingRequirement> CraftingRequirements { get; set; } = [];
}
```

**Step 3: Update Game entity**

Change `ICollection<Building> Buildings` to `ICollection<GameBuilding> GameBuildings`.

### Task 1.2: Create EF configuration and update DbContext

**Files:**
- Create: `src/OvcinaHra.Api/Data/Configurations/GameBuildingConfiguration.cs`
- Modify: `src/OvcinaHra.Api/Data/Configurations/BuildingConfiguration.cs` — remove Game FK
- Modify: `src/OvcinaHra.Api/Data/WorldDbContext.cs` — add `DbSet<GameBuilding>`

**Step 1: Create GameBuildingConfiguration**

```csharp
public class GameBuildingConfiguration : IEntityTypeConfiguration<GameBuilding>
{
    public void Configure(EntityTypeBuilder<GameBuilding> builder)
    {
        builder.HasKey(e => new { e.GameId, e.BuildingId });
        builder.HasOne(e => e.Game).WithMany(g => g.GameBuildings).HasForeignKey(e => e.GameId);
        builder.HasOne(e => e.Building).WithMany(b => b.GameBuildings).HasForeignKey(e => e.BuildingId);
        builder.HasIndex(e => e.GameId);
    }
}
```

**Step 2: Update BuildingConfiguration** — remove `.HasOne(e => e.Game)` line.

**Step 3: Add to WorldDbContext:** `public DbSet<GameBuilding> GameBuildings => Set<GameBuilding>();`

### Task 1.3: Create migration

**Step 1:** Run `dotnet ef migrations add BuildingCatalogRefactor --project src/OvcinaHra.Api --startup-project src/OvcinaHra.Api`

**Step 2:** Verify migration compiles: `dotnet build src/OvcinaHra.Api`

### Task 1.4: Update Building DTOs

**Files:**
- Modify: `src/OvcinaHra.Shared/Dtos/BuildingDtos.cs`

Remove `GameId` from `BuildingListDto` and `BuildingDetailDto`. Update `CreateBuildingDto` — `GameId` is no longer part of the building itself (it's assigned via GameBuilding). Keep `GameId` on `CreateBuildingDto` as optional context for auto-assignment.

```csharp
public record BuildingListDto(int Id, string Name, int? LocationId, string? LocationName, bool IsPrebuilt);
public record BuildingDetailDto(int Id, string Name, string? Description, string? ImagePath, int? LocationId, bool IsPrebuilt);
public record CreateBuildingDto(string Name, string? Description = null, int? LocationId = null, bool IsPrebuilt = false);
public record UpdateBuildingDto(string Name, string? Description, int? LocationId, bool IsPrebuilt);
```

### Task 1.5: Update BuildingEndpoints

**Files:**
- Modify: `src/OvcinaHra.Api/Endpoints/BuildingEndpoints.cs`

Add assign/unassign routes (same pattern as LocationEndpoints):
```
GET  /api/buildings              — GetAll (catalog)
GET  /api/buildings/{id}         — GetById
POST /api/buildings              — Create
PUT  /api/buildings/{id}         — Update
DELETE /api/buildings/{id}       — Delete
GET  /api/buildings/by-game/{gameId}                    — GetByGame
POST /api/buildings/by-game                             — AssignToGame
DELETE /api/buildings/by-game/{gameId}/{buildingId}      — RemoveFromGame
```

`GetByGame` joins through `GameBuildings`. `Create` optionally accepts `gameId` query param and auto-assigns.

### Task 1.6: Update BuildingEndpointTests

**Files:**
- Modify: `tests/OvcinaHra.Api.Tests/Endpoints/BuildingEndpointTests.cs`

Update existing tests for new DTO shape (no GameId in list/detail). Add tests:
- `AssignToGame_ReturnsCreated`
- `GetByGame_ReturnsOnlyAssigned`
- `RemoveFromGame_ReturnsNoContent`
- `Create_WithGameId_AutoAssigns`

**Step: Run all tests** — `dotnet test tests/OvcinaHra.Api.Tests`

**Commit:** `"refactor: Building to catalog model with GameBuilding join table"`

---

## Phase 2: Sidebar Navigation Restructure

### Task 2.1: Update NavMenu with two sections

**Files:**
- Modify: `src/OvcinaHra.Client/Layout/NavMenu.razor`

Restructure the sidebar:

```
Hra (active game)
  🗺️ Mapa             → /map
  📍 Lokace           → /locations
  ⚔️ Předměty         → /items
  🧌 Příšery          → /monsters (global)
  📜 Questy           → /quests
  🏗️ Budovy           → /buildings
  💎 Poklady          → /treasures

Katalog světa
  📍 Lokace           → /locations?catalog=true
  ⚔️ Předměty         → /items?catalog=true
  🏗️ Budovy           → /buildings?catalog=true
  📜 Questy           → /quests?catalog=true

Nástroje
  🏷️ Tagy             → /tags
  🔍 Hledání          → /search
  ⚙️ Seznam Ovčin     → /games
```

**Commit:** `"feat: restructure sidebar into game/catalog/tools sections"`

---

## Phase 3: Catalog Toggle on List Pages

### Task 3.1: LocationList — add catalog toggle

**Files:**
- Modify: `src/OvcinaHra.Client/Pages/Locations/LocationList.razor`

**Changes:**
1. Add `[SupplyParameterFromQuery] public bool Catalog { get; set; }` parameter
2. Add toggle button in header: "Katalog" / "Jen tato hra"
3. When `Catalog = false` (default): load from `/api/locations/by-game/{gameId}`
4. When `Catalog = true`: load from `/api/locations` (all)
5. In catalog mode, show "Přiřadit" / "Odebrat" button per row (or checkbox column)
6. Creating a location in game mode auto-assigns via `POST /api/locations/by-game`

**Commit:** `"feat: LocationList catalog toggle with assign/unassign"`

### Task 3.2: ItemList — add catalog toggle

**Files:**
- Modify: `src/OvcinaHra.Client/Pages/Items/ItemList.razor`

Same pattern as LocationList. Items already have `GET /api/items` (all) and `GET /api/items/by-game/{gameId}`.

In catalog mode, unassigned items show an "Přiřadit" button that creates a GameItem.
Creating an item in game mode: `POST /api/items` + `POST /api/items/game-item`.

**Note:** ItemList already has the per-game GameItem config section. In catalog mode, this section is hidden (no game context for unassigned items).

**Commit:** `"feat: ItemList catalog toggle with assign/unassign"`

### Task 3.3: BuildingList — add catalog toggle

**Files:**
- Modify: `src/OvcinaHra.Client/Pages/Buildings/BuildingList.razor`

Same pattern. Use new `GET /api/buildings` (all) and `GET /api/buildings/by-game/{gameId}`.

**Commit:** `"feat: BuildingList catalog toggle with assign/unassign"`

### Task 3.4: QuestList — add catalog toggle with copy picker

**Files:**
- Modify: `src/OvcinaHra.Client/Pages/Quests/QuestList.razor`
- Create: `src/OvcinaHra.Api/Endpoints/QuestCopyEndpoints.cs` (or add to QuestEndpoints)

**API changes needed:**
- `GET /api/quests/all` — returns quests across all games, grouped by game
- `POST /api/quests/{id}/copy-to-game/{gameId}` — copies quest + sub-entities to target game

**Copy logic (server-side):**
1. Load source quest with Tags, Locations, Encounters, Rewards
2. Create new Quest with same fields, new GameId, reset ChainOrder/ParentQuestId to null
3. Copy tag links (tags are global)
4. Copy encounter links (monsters are global)
5. Skip location links where location is not in target game (via GameLocation)
6. Skip reward links where item is not in target game (via GameItem)
7. Return the new quest + warnings about skipped links

**DTO:**
```csharp
public record QuestCopyResultDto(QuestDetailDto Quest, List<string> Warnings);
```

**UI in catalog mode:**
- Grid shows quests from all games with a "Hra" column showing game name
- Click row → popup with quest details + "Kopírovat do aktuální hry" button
- After copy, show warnings if any links were skipped

**Commit:** `"feat: QuestList catalog toggle with cross-game copy"`

---

## Phase 4: Auto-Assign on Create in Game View

### Task 4.1: Location — auto-assign on create

**Files:**
- Modify: `src/OvcinaHra.Client/Pages/Locations/LocationList.razor`

When creating a location in game mode (not catalog): after `POST /api/locations`, also call `POST /api/locations/by-game` with `{gameId, newLocationId}`.

### Task 4.2: Building — auto-assign on create

**Files:**
- Modify: `src/OvcinaHra.Client/Pages/Buildings/BuildingList.razor`

Same pattern — after `POST /api/buildings`, also call `POST /api/buildings/by-game`.

### Task 4.3: Item — auto-assign on create

**Files:**
- Modify: `src/OvcinaHra.Client/Pages/Items/ItemList.razor`

After `POST /api/items`, also call `POST /api/items/game-item` with default GameItem values.

**Commit:** `"feat: auto-assign new entities to current game"`

---

## Phase 5: Integration Tests

### Task 5.1: Building catalog tests

**Files:**
- Modify: `tests/OvcinaHra.Api.Tests/Endpoints/BuildingEndpointTests.cs`

Test the full catalog flow:
- `GetAll_ReturnsAllBuildings` (catalog)
- `GetByGame_ReturnsOnlyAssigned`
- `AssignToGame_ReturnsCreated`
- `RemoveFromGame_ReturnsNoContent`
- `Create_ThenAssign_Works`

### Task 5.2: Quest copy tests

**Files:**
- Create: `tests/OvcinaHra.Api.Tests/Endpoints/QuestCopyTests.cs`

Test copy logic:
- `CopyQuest_CopiesBasicFields`
- `CopyQuest_CopiesTags`
- `CopyQuest_CopiesEncounters`
- `CopyQuest_SkipsLocationNotInTargetGame`
- `CopyQuest_SkipsRewardNotInTargetGame`
- `CopyQuest_ResetsChainFields`

### Task 5.3: Run full test suite

Run: `dotnet test tests/OvcinaHra.Api.Tests`
Expected: All tests pass.

**Commit:** `"test: catalog and quest copy integration tests"`

---

## Phase 6: Cleanup

### Task 6.1: Update CraftingRecipe references

CraftingRecipe has `GameId` — it's game-scoped (recipes can differ per game). Verify it still works after Building model change, since CraftingBuildingRequirement references Building.

### Task 6.2: Verify treasure planning board

The treasure planning board uses `GameLocation` and `GameItem` — verify it still works correctly with the refactored model.

### Task 6.3: Update seed/import endpoints

If any seed endpoints reference `Building.GameId`, update them.

**Commit:** `"chore: cleanup after catalog refactor"`

---

## Summary

| Phase | Tasks | Scope |
|---|---|---|
| 1 | 1.1–1.6 | Building → GameBuilding join table + migration |
| 2 | 2.1 | Sidebar restructure (game/catalog/tools) |
| 3 | 3.1–3.4 | Catalog toggle on Location, Item, Building, Quest pages |
| 4 | 4.1–4.3 | Auto-assign on create in game view |
| 5 | 5.1–5.3 | Integration tests for new flows |
| 6 | 6.1–6.3 | Cleanup and verification |

**Estimated tasks:** 19 tasks across 6 phases
**Dependencies:** Phase 1 must complete before Phase 3.3 (BuildingList). Phase 2 can run in parallel with Phase 1. Phases 3-4 depend on Phase 1+2. Phase 5 after Phase 3-4.
