# Skills Domain Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Introduce the Skill domain (global catalog + per-game knobs) and wire it into the crafting recipe editor so a recipe can require 1..N skills.

**Architecture:** Global `Skill` catalog (name, class restriction, effect, building requirements, notes) with a per-game `GameSkill` join that holds XP cost and level requirement. New `CraftingSkillRequirement` join links recipes to skills (referencing global Skill, not GameSkill, for consistency with existing recipe pattern). All CRUD via minimal API endpoints, all UI via `OvcinaGrid` + `DxPopup` pattern established elsewhere in the app.

**Tech Stack:** .NET 10 minimal API, EF Core 10 + Npgsql, xUnit + Testcontainers PostgreSQL, Blazor WASM PWA, DevExpress Blazor grid/form controls, Playwright E2E.

**Design doc:** `docs/plans/2026-04-19-skills-domain-design.md` — read this first before starting.

**Project conventions (from `.claude/CLAUDE.md` and design doc):**

- Code in English, UI in Czech with proper diacritics
- Page routes in English (`/skills`, `/games/{id}/skills`)
- Enums stored as strings via `.HasConversion<string>()`; all enums have `[JsonConverter(typeof(JsonStringEnumConverter))]`
- Composite PKs on join tables (no surrogates)
- Every CRUD endpoint group has integration tests with Testcontainers PostgreSQL
- Commit after each task; include `[vX.Y.Z]` in commit messages (bump patch for each phase completion)
- Hrdina/Hrdinové when referring to the player; "Dobrodruh" used only as skill category label

---

## Phase 1 — Domain entities, EF Core config, migration

### Task 1.1: Add `Skill` entity

**Files:**
- Create: `src/OvcinaHra.Shared/Domain/Entities/Skill.cs`

**Step 1:** Write the entity.

```csharp
using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Shared.Domain.Entities;

public class Skill
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public PlayerClass? ClassRestriction { get; set; }
    public string? Effect { get; set; }
    public string? RequirementNotes { get; set; }
    public string? ImagePath { get; set; }

    public ICollection<SkillBuildingRequirement> BuildingRequirements { get; set; } = [];
    public ICollection<GameSkill> GameSkills { get; set; } = [];
    public ICollection<CraftingSkillRequirement> CraftingRequirements { get; set; } = [];
}
```

**Step 2:** Verify it compiles.
Run: `dotnet build src/OvcinaHra.Shared/OvcinaHra.Shared.csproj`
Expected: build succeeds.

**Step 3:** Commit.
```bash
git add src/OvcinaHra.Shared/Domain/Entities/Skill.cs
git commit -m "feat(skills): add Skill entity [v0.3.0-dev]"
```

---

### Task 1.2: Add `SkillBuildingRequirement` entity

**Files:**
- Create: `src/OvcinaHra.Shared/Domain/Entities/SkillBuildingRequirement.cs`

**Step 1:** Write the entity (matches `CraftingBuildingRequirement` shape).

```csharp
namespace OvcinaHra.Shared.Domain.Entities;

public class SkillBuildingRequirement
{
    public int SkillId { get; set; }
    public int BuildingId { get; set; }

    public Skill Skill { get; set; } = null!;
    public Building Building { get; set; } = null!;
}
```

**Step 2:** Build + commit.
```bash
dotnet build src/OvcinaHra.Shared/OvcinaHra.Shared.csproj
git add src/OvcinaHra.Shared/Domain/Entities/SkillBuildingRequirement.cs
git commit -m "feat(skills): add SkillBuildingRequirement join entity [v0.3.0-dev]"
```

---

### Task 1.3: Add `GameSkill` entity

**Files:**
- Create: `src/OvcinaHra.Shared/Domain/Entities/GameSkill.cs`

```csharp
namespace OvcinaHra.Shared.Domain.Entities;

public class GameSkill
{
    public int GameId { get; set; }
    public int SkillId { get; set; }
    public int XpCost { get; set; }
    public int? LevelRequirement { get; set; }

    public Game Game { get; set; } = null!;
    public Skill Skill { get; set; } = null!;
}
```

Build + commit analogously.

---

### Task 1.4: Add `CraftingSkillRequirement` entity

**Files:**
- Create: `src/OvcinaHra.Shared/Domain/Entities/CraftingSkillRequirement.cs`

```csharp
namespace OvcinaHra.Shared.Domain.Entities;

public class CraftingSkillRequirement
{
    public int CraftingRecipeId { get; set; }
    public int SkillId { get; set; }

    public CraftingRecipe CraftingRecipe { get; set; } = null!;
    public Skill Skill { get; set; } = null!;
}
```

Build + commit analogously.

---

### Task 1.5: Extend `CraftingRecipe` with navigation to required skills

**Files:**
- Modify: `src/OvcinaHra.Shared/Domain/Entities/CraftingRecipe.cs`

**Step 1:** Add one line to the navigation section:
```csharp
public ICollection<CraftingSkillRequirement> SkillRequirements { get; set; } = [];
```

**Step 2:** Build + commit.

---

### Task 1.6: Write `SkillConfiguration` (EF Core)

**Files:**
- Create: `src/OvcinaHra.Api/Data/Configurations/SkillConfiguration.cs`

**Reference pattern:** look at `ItemConfiguration.cs` in the same folder — mirror its shape.

Key details:
- `builder.HasKey(x => x.Id)`
- `Name` required, max length 100
- `ClassRestriction` stored as string: `.HasConversion<string>().HasMaxLength(20)`
- `Effect` / `RequirementNotes` / `ImagePath` optional, max length 1000 / 500 / 500 respectively
- Unique index on `Name`: `builder.HasIndex(x => x.Name).IsUnique()`
- Cascade delete to `BuildingRequirements`, `GameSkills`, `CraftingRequirements`

**Step 1:** Write config.

**Step 2:** Register in `WorldDbContext.OnModelCreating` (if manual registration used) or verify assembly-scan picks it up — check how `ItemConfiguration` is wired and match.

**Step 3:** Build + commit.
```bash
git add src/OvcinaHra.Api/Data/Configurations/SkillConfiguration.cs
git commit -m "feat(skills): add Skill EF Core configuration [v0.3.0-dev]"
```

---

### Task 1.7: Write `SkillBuildingRequirementConfiguration`

**Files:**
- Create: `src/OvcinaHra.Api/Data/Configurations/SkillBuildingRequirementConfiguration.cs`

**Reference pattern:** the `CraftingBuildingRequirement` portion of `CraftingConfiguration.cs` — composite PK, two FK relationships with cascade behavior from parent.

Key details:
- `builder.HasKey(x => new { x.SkillId, x.BuildingId })`
- FK to `Skill` with cascade delete
- FK to `Building` with restrict delete (so you can't delete a building that a skill needs — or cascade, match what `CraftingBuildingRequirement` does)

Build + commit.

---

### Task 1.8: Write `GameSkillConfiguration`

**Files:**
- Create: `src/OvcinaHra.Api/Data/Configurations/GameSkillConfiguration.cs`

**Reference pattern:** `GameItemConfiguration.cs` — composite PK + CHECK constraints via `builder.ToTable(t => t.HasCheckConstraint(...))`.

Key details:
- `builder.HasKey(x => new { x.GameId, x.SkillId })`
- CHECK constraints:
  - `CK_GameSkill_XpCost_NonNegative` → `"XpCost" >= 0`
  - `CK_GameSkill_LevelRequirement_NonNegative` → `"LevelRequirement" IS NULL OR "LevelRequirement" >= 0`
- FK to Game (cascade), FK to Skill (cascade)

Build + commit.

---

### Task 1.9: Write `CraftingSkillRequirementConfiguration`

**Files:**
- Create: `src/OvcinaHra.Api/Data/Configurations/CraftingSkillRequirementConfiguration.cs`

Same shape as `SkillBuildingRequirementConfiguration` but for `CraftingRecipe` ↔ `Skill`. Composite PK on `(CraftingRecipeId, SkillId)`. Cascade from recipe, cascade or restrict from skill (match `CraftingBuildingRequirement` precedent).

Build + commit.

---

### Task 1.10: Add `DbSet<Skill>` and related sets to `WorldDbContext`

**Files:**
- Modify: `src/OvcinaHra.Api/Data/WorldDbContext.cs`

Add four DbSets:
```csharp
public DbSet<Skill> Skills => Set<Skill>();
public DbSet<SkillBuildingRequirement> SkillBuildingRequirements => Set<SkillBuildingRequirement>();
public DbSet<GameSkill> GameSkills => Set<GameSkill>();
public DbSet<CraftingSkillRequirement> CraftingSkillRequirements => Set<CraftingSkillRequirement>();
```

Build + commit.

---

### Task 1.11: Generate EF Core migration

**Files:**
- Migration generated under `src/OvcinaHra.Api/Migrations/`

**Step 1:** From the API project directory:
```bash
cd src/OvcinaHra.Api
dotnet ef migrations add AddSkillsDomain
```

**Step 2:** Review the generated migration file. Verify it creates:
- `Skills`, `SkillBuildingRequirements`, `GameSkills`, `CraftingSkillRequirements` tables
- The two CHECK constraints on `GameSkills`
- Unique index on `Skills.Name`
- All FK relationships with the correct cascade behavior

**Step 3:** Apply to dev DB:
```bash
dotnet ef database update
```
Expected: no errors; tables appear in Postgres.

**Step 4:** Commit.
```bash
git add src/OvcinaHra.Api/Migrations/
git commit -m "feat(skills): EF Core migration AddSkillsDomain [v0.3.0-dev]"
```

---

## Phase 2 — Skill catalog API

### Task 2.1: Write DTOs for skills

**Files:**
- Create: `src/OvcinaHra.Shared/Dtos/SkillDtos.cs`

**Reference pattern:** `ItemDtos.cs`.

```csharp
using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Shared.Dtos;

public record SkillDto(
    int Id,
    string Name,
    PlayerClass? ClassRestriction,
    string? Effect,
    string? RequirementNotes,
    string? ImagePath,
    IReadOnlyList<int> RequiredBuildingIds);

public record CreateSkillRequest(
    string Name,
    PlayerClass? ClassRestriction,
    string? Effect,
    string? RequirementNotes,
    IReadOnlyList<int> RequiredBuildingIds);

public record UpdateSkillRequest(
    string Name,
    PlayerClass? ClassRestriction,
    string? Effect,
    string? RequirementNotes,
    IReadOnlyList<int> RequiredBuildingIds);
```

Build + commit.

---

### Task 2.2: Write failing tests for POST `/api/skills`

**Files:**
- Create: `tests/OvcinaHra.Api.Tests/Endpoints/SkillEndpointsTests.cs`

**Reference pattern:** existing endpoint test files under `tests/OvcinaHra.Api.Tests/Endpoints/`. Use the same test fixture (Testcontainers PostgreSQL) they use.

Initial cases:
1. `Post_CreatesClassSkill_ReturnsCreated` — POST with `ClassRestriction = Thief`, assert 201 + returned DTO has ClassRestriction set + skill is in DB
2. `Post_CreatesAdventurerSkill_NullClassRestriction` — POST with null ClassRestriction, assert returned DTO has null
3. `Post_WithBuildingRequirements_PersistsAll` — POST with 2 building ids, assert persisted join rows
4. `Post_DuplicateName_Returns409` — POST same name twice, assert 409

Run: `dotnet test tests/OvcinaHra.Api.Tests/ --filter "FullyQualifiedName~SkillEndpointsTests"`
Expected: all four FAIL (endpoints don't exist).

Commit the failing tests:
```bash
git add tests/OvcinaHra.Api.Tests/Endpoints/SkillEndpointsTests.cs
git commit -m "test(skills): failing tests for skill POST endpoint [v0.3.0-dev]"
```

---

### Task 2.3: Implement POST `/api/skills`

**Files:**
- Create: `src/OvcinaHra.Api/Endpoints/SkillEndpoints.cs`
- Modify: `src/OvcinaHra.Api/Program.cs` (register `app.MapSkillEndpoints();`)

**Reference pattern:** `ItemEndpoints.cs` — copy its `MapItemEndpoints` extension method shape.

**Step 1:** Write `SkillEndpoints.cs` with only POST and the extension method registered with the MapGroup. Use `TypedResults.Created`. Validate Name non-empty; convert incoming RequiredBuildingIds into `SkillBuildingRequirement` rows. Handle unique constraint violation as 409.

**Step 2:** Wire in `Program.cs` next to the other `Map*Endpoints()` calls.

**Step 3:** Run the tests again:
```bash
dotnet test tests/OvcinaHra.Api.Tests/ --filter "FullyQualifiedName~SkillEndpointsTests"
```
Expected: all four PASS.

**Step 4:** Commit.
```bash
git add src/OvcinaHra.Api/Endpoints/SkillEndpoints.cs src/OvcinaHra.Api/Program.cs
git commit -m "feat(skills): POST /api/skills endpoint [v0.3.0-dev]"
```

---

### Task 2.4: GET `/api/skills` and `/api/skills/{id}` — test then implement

**Step 1:** Add tests to `SkillEndpointsTests.cs`:
- `Get_ReturnsAllSkills`
- `GetById_ReturnsSkillWithBuildingIds`
- `GetById_NotFound_Returns404`

Run — expect fail.

**Step 2:** Add handlers to `SkillEndpoints.cs`. Eager-load `BuildingRequirements` for the detail endpoint; for list you may also include them (data volume is tiny — dozens of skills max).

Run — expect pass. Commit.

---

### Task 2.5: PUT `/api/skills/{id}` — test then implement

**Step 1:** Tests:
- `Put_UpdatesScalarFields`
- `Put_ReplacesBuildingRequirementsAsSet` — start with building ids `[1,2]`, PUT with `[2,3]`, assert final set is `[2,3]`
- `Put_NonExistentId_Returns404`
- `Put_DuplicateName_Returns409` (renaming to an existing skill's name)

**Step 2:** Handler: load skill + BuildingRequirements, update scalars, diff and rewrite the join set, save.

Run tests → pass → commit.

---

### Task 2.6: DELETE `/api/skills/{id}` — test then implement

**Step 1:** Tests:
- `Delete_RemovesSkill`
- `Delete_WithRecipeReference_Returns409` — create recipe with this skill as required, attempt delete, assert 409 + skill still in DB
- `Delete_WithGameSkillReference_Returns409` — same idea but via GameSkill

**Step 2:** Handler: check for referencing rows first, 409 with Czech message if found, else delete.

Run tests → pass → commit.

---

## Phase 3 — Per-game skill API (GameSkill)

### Task 3.1: DTOs for GameSkill

**Files:**
- Modify: `src/OvcinaHra.Shared/Dtos/GameDtos.cs` (or create a new `GameSkillDtos.cs` if that file is already long)

```csharp
public record GameSkillDto(
    int GameId,
    int SkillId,
    string SkillName,
    PlayerClass? ClassRestriction,
    int XpCost,
    int? LevelRequirement);

public record UpsertGameSkillRequest(int XpCost, int? LevelRequirement);
```

Build + commit.

---

### Task 3.2: Failing tests for per-game skill endpoints

**Files:**
- Create: `tests/OvcinaHra.Api.Tests/Endpoints/GameSkillEndpointsTests.cs`

Cases:
1. `Put_AddsSkillToGame`
2. `Put_SecondCall_UpdatesXpAndLevel`
3. `Put_NegativeXpCost_Returns400`
4. `Put_NegativeLevelRequirement_Returns400`
5. `Put_LevelRequirementNull_Accepted`
6. `Get_ListsOnlySkillsInGame`
7. `Delete_RemovesSkillFromGame`
8. `Delete_BlockedWhenRecipeRequiresSkill_Returns409` — set up a recipe in the same game using the skill, attempt delete, assert 409

Run → fail. Commit.

---

### Task 3.3: Implement per-game skill endpoints

**Files:**
- Modify: `src/OvcinaHra.Api/Endpoints/GameEndpoints.cs` (add the three routes under the existing `/api/games/{gameId}` group — follow the GameItem pattern if it exists there, otherwise copy the shape from an analogous per-game endpoint)

Routes:
- GET `/api/games/{gameId}/skills`
- PUT `/api/games/{gameId}/skills/{skillId}` (upsert)
- DELETE `/api/games/{gameId}/skills/{skillId}`

For DELETE: query `CraftingSkillRequirements.Any(cs => cs.SkillId == skillId && cs.CraftingRecipe.GameId == gameId)` — if true, return 409.

Run tests → pass → commit.

---

## Phase 4 — Crafting recipe integration

### Task 4.1: Extend crafting DTOs

**Files:**
- Modify: `src/OvcinaHra.Shared/Dtos/CraftingDtos.cs`

Add `IReadOnlyList<int> RequiredSkillIds` to:
- `CraftingRecipeDto`
- `CreateCraftingRecipeRequest` (or whatever the create/upsert request is named)
- `UpdateCraftingRecipeRequest`

Build + commit.

---

### Task 4.2: Failing tests for recipe skill requirements

**Files:**
- Modify: `tests/OvcinaHra.Api.Tests/Endpoints/CraftingEndpointsTests.cs` (or the existing crafting tests file — find it)

Add cases:
- `CreateRecipe_WithRequiredSkillIds_PersistsLinks`
- `UpdateRecipe_ReplacesSkillSet` (start `[1,2]`, update to `[2,3]`, assert final `[2,3]`)
- `CreateRecipe_SkillNotInGame_Returns400` — skill exists globally but no GameSkill row for this game
- `CreateRecipe_UnknownSkillId_Returns400`

Run → fail. Commit.

---

### Task 4.3: Implement recipe skill requirement persistence + validation

**Files:**
- Modify: `src/OvcinaHra.Api/Endpoints/CraftingEndpoints.cs`

Validation block before save (both POST and PUT):
```csharp
var skillIds = request.RequiredSkillIds.Distinct().ToArray();
if (skillIds.Length > 0)
{
    var activeSkillIds = await db.GameSkills
        .Where(gs => gs.GameId == gameId && skillIds.Contains(gs.SkillId))
        .Select(gs => gs.SkillId)
        .ToArrayAsync(ct);

    var missing = skillIds.Except(activeSkillIds).ToArray();
    if (missing.Length > 0)
        return TypedResults.BadRequest($"Dovednosti nejsou ve hře dostupné: [{string.Join(", ", missing)}]");
}
```

On update: replace `CraftingSkillRequirements` set same way the `CraftingBuildingRequirements` is replaced.

Include `RequiredSkillIds` in all responses (projection from `SkillRequirements`).

Run tests → pass → commit.

---

## Phase 5 — Shared client helpers + API client wrapper

### Task 5.1: `GetClassRestrictionLabel` extension

**Files:**
- Create: `src/OvcinaHra.Shared/Extensions/PlayerClassExtensions.cs`

```csharp
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Shared.Extensions;

public static class PlayerClassExtensions
{
    public static string GetClassRestrictionLabel(this PlayerClass? pc)
        => pc?.GetDisplayName() ?? "Dobrodruh";

    // If GetDisplayName isn't already an extension in this project,
    // follow the pattern already used wherever PlayerClass is rendered today
    // (search for `GetDisplayName` in the codebase first).
}
```

If `GetDisplayName()` doesn't exist, add it as well in the same file using `Enum.GetMember` + `DisplayAttribute`. Otherwise reuse the existing helper.

Build + commit.

---

### Task 5.2: API client wrapper for skills

**Files:**
- Create: `src/OvcinaHra.Client/Services/SkillService.cs`

**Reference pattern:** look in `src/OvcinaHra.Client/Services/` for existing services (ItemService, BuildingService, etc.) and mirror.

Methods:
- `Task<IReadOnlyList<SkillDto>> GetAllAsync()`
- `Task<SkillDto?> GetByIdAsync(int id)`
- `Task<SkillDto> CreateAsync(CreateSkillRequest)`
- `Task<SkillDto> UpdateAsync(int id, UpdateSkillRequest)`
- `Task DeleteAsync(int id)`
- `Task<IReadOnlyList<GameSkillDto>> GetGameSkillsAsync(int gameId)`
- `Task UpsertGameSkillAsync(int gameId, int skillId, UpsertGameSkillRequest)`
- `Task RemoveGameSkillAsync(int gameId, int skillId)`

**Step 2:** Register in `Program.cs` (Client) next to the existing service registrations.

Build + commit.

---

## Phase 6 — Skills catalog page

### Task 6.1: `Skills.razor` list + detail popup

**Files:**
- Create: `src/OvcinaHra.Client/Pages/Skills/Skills.razor`
- Create: `src/OvcinaHra.Client/Pages/Skills/Skills.razor.cs` (code-behind if the project uses that pattern — check other pages)

**Reference pattern:** `src/OvcinaHra.Client/Pages/Items/ItemList.razor` — copy structure.

Grid columns (use `OvcinaGrid`):
- Název
- Povolání — `CellDisplayTemplate` calls `skill.ClassRestriction.GetClassRestrictionLabel()`
- Efekt (truncated if long)
- Požadované budovy — comma-joined names (fetch buildings lookup up-front for render)
- Poznámka

Row click opens `DxPopup` with `DxFormLayout`:
- Název — `DxTextBox`
- Typ — two radio buttons: "Povolání" (shows a `DxComboBox` of `PlayerClass` values) / "Dobrodružná" (sets `ClassRestriction = null`)
- Efekt — `DxMemo`
- Poznámka k požadavkům — `DxMemo`
- Požadované budovy — list with "+ přidat budovu" dropdown mirroring the recipe pattern in the item dialog
- Obrázek — upload (reuse the same upload component as Items)

Save / Cancel / Delete buttons in footer. Separate `DxPopup` for delete confirmation.

**Step 2:** Register the page route. Add a link "Dovednosti" to the navigation menu (find where Items/Monsters links are defined and add alongside).

**Step 3:** Run the app (`devStart.bat`), create one class skill + one adventurer skill, edit both, delete one. Verify persistence by reloading.

**Step 4:** Commit.

---

## Phase 7 — Per-game skills page

### Task 7.1: `GameSkills.razor` — manage skills active in a game

**Files:**
- Create: `src/OvcinaHra.Client/Pages/Games/GameSkills.razor` (+ code-behind if used)

Route: `/games/{GameId:int}/skills`.

Grid (`OvcinaGrid`) of `GameSkillDto` rows: Název, Povolání, XP, Úroveň, [×] remove button.

Above grid: "+ Přidat dovednost ze seznamu" — `DxComboBox` populated with all `Skill`s not currently in this game. Clicking + opens a small popup for XP and level, then calls upsert.

Row click opens popup to edit XP / LevelRequirement.

Add link to this page from the Game detail view (find the existing game detail layout).

**Step 2:** Run app, open a game, add two skills with different XP/level, change XP on one, remove one. Reload to verify.

**Step 3:** Commit.

---

## Phase 8 — Crafting dialog integration

### Task 8.1: Add required-skills block to recipe editor

**Files:**
- Modify: `src/OvcinaHra.Client/Pages/Items/ItemList.razor` (or wherever the recipe editor popup lives — find by grep for "Smazat recept" or "Požadované budovy")

Between "Požadované budovy" and "Smazat recept", insert a block:

```razor
<div class="recipe-skill-requirements">
  <strong>Požadované dovednosti:</strong>
  @if (Recipe.RequiredSkillIds.Count == 0) { <span>Žádné.</span> }
  <ul>
    @foreach (var skillId in Recipe.RequiredSkillIds)
    {
      var skill = gameSkills.FirstOrDefault(gs => gs.SkillId == skillId);
      <li>@skill?.SkillName <button @onclick="() => RemoveSkill(skillId)">×</button></li>
    }
  </ul>
  <DxComboBox Data="availableGameSkills" @bind-Value="selectedSkillId" />
  <button @onclick="AddSkill">+</button>
</div>
```

On popup open: fetch `GameSkillDto[]` for the current game and filter by `!Recipe.RequiredSkillIds.Contains(gs.SkillId)` for the dropdown source.

Include `RequiredSkillIds` in the save payload (the recipe DTO already has it after Phase 4).

**Step 2:** Run app. Create a skill, add it to a game, open an item recipe, add skill requirement, save, reload. Verify persistence and that the dropdown only shows game-active skills.

**Step 3:** Commit.

---

## Phase 9 — E2E test

### Task 9.1: Playwright spec for the full flow

**Files:**
- Create: `tests/OvcinaHra.E2E/skills.spec.ts` (path depends on project convention — check existing specs)

Flow (single test):
1. Navigate to `/skills`, create a new class skill (Zloděj, "Tichý úder").
2. Navigate to `/games/{id}/skills`, add the skill with XpCost=10, LevelRequirement=2.
3. Open an item's recipe in the crafting dialog, add the skill as required, save.
4. Reload the item, confirm the skill appears in the list.

Run: `cd tests/OvcinaHra.E2E && npx playwright test skills.spec.ts`
Expected: PASS.

Commit.

---

## Phase 10 — Rulemaster bot handoff (cross-repo)

### Task 10.1: Write handoff note for rulemaster integration

**Files:**
- Create: `docs/plans/2026-04-19-rulemaster-skills-followup.md`

One-page handoff:
- Summary of endpoints added (paths + DTO shapes)
- Note that OpenAPI auto-exposes them
- Action: locate rulemaster config (likely `.claude/skills/rulemaster-ovcina/`) in whichever repo it lives, add the new endpoints to the same place items are listed, re-test bot.

This is documentation only — no code change in ovcinahra. Commit the note, done.

---

## Completion checklist

- [ ] All entities + configs created, migration generated and applied
- [ ] `dotnet build` clean at solution level
- [ ] All integration tests pass: `dotnet test tests/OvcinaHra.Api.Tests/`
- [ ] All endpoint groups exercised (POST/GET/PUT/DELETE for skills, GET/PUT/DELETE for game skills, extended crafting POST/PUT)
- [ ] Manual UI smoke (skills page, per-game skills page, crafting dialog)
- [ ] Playwright `skills.spec.ts` passes
- [ ] Rulemaster handoff note written
- [ ] Version bumped appropriately before any deploy

## Notes for the implementer

- Follow project's "commit-after-every-task" convention. Each task above is a single commit.
- The design doc at `docs/plans/2026-04-19-skills-domain-design.md` is the source of truth for any ambiguity. If you feel tempted to deviate, re-read the design first.
- The two sources I cite most ("mirror ItemConfiguration", "mirror ItemEndpoints") are the load-bearing patterns — deviate from them only for skill-specific reasons.
- Czech UI copy must use proper diacritics. If unsure about a string, ask.
- `Hrdina` / `Hrdinové` for any player reference; `Dobrodruh` is reserved for the adventurer-skill category label.
