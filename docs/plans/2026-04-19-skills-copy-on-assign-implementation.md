# Skills Copy-on-Assign — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Turn `GameSkill` from a pointer-to-`Skill` into a standalone editable copy, so per-game edits (Name, Effect, Notes, Class, Buildings, XP, Level) stay isolated to that game. Update API, UI, and migration accordingly.

**Architecture:** Single replacement migration (no prod data to preserve). `GameSkill` gains a surrogate `Id`, a nullable `TemplateSkillId`, and all the scalar fields that were previously only on `Skill`. New join `GameSkillBuildingRequirement`. `CraftingSkillRequirement` switches its reference from `SkillId` to `GameSkillId`. Skills catalog page (`/skills`) stays essentially the same — just adds a banner. Per-game page (`/games/{id}/skills`) gets a richer popup and an add-from-template vs. custom chooser. NavMenu Hra-section link becomes dynamic via `GameContextService`.

**Tech Stack:** .NET 10, EF Core 10 + Npgsql, Blazor WASM, DevExpress Blazor, xUnit + Testcontainers PostgreSQL.

**Design doc:** `docs/plans/2026-04-19-skills-copy-on-assign-design.md` — read first before Phase 1.

**Project conventions:**
- Code in English, UI in Czech with diacritics
- Enums stored as strings via EF Core; DTO enums serialized as strings via `[JsonConverter(typeof(JsonStringEnumConverter))]`
- Composite PKs on pure-join tables; surrogate `Id` when the entity is first-class
- Commit after every task; `[v0.7.0]` version suffix in commit messages (breaking data-model change = minor bump)
- Run `dotnet build src/OvcinaHra.Api/OvcinaHra.Api.csproj` after each code change; 0 errors expected

**Branch:** `feat/skills-copy-on-assign` (already created from main @ `8892d73`).

---

## Phase 1 — Domain + migration

### Task 1.1: Add surrogate `Id` + new fields to `GameSkill` entity

**File:** modify `src/OvcinaHra.Shared/Domain/Entities/GameSkill.cs`

Replace the current entity with:

```csharp
using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Shared.Domain.Entities;

public class GameSkill
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public int? TemplateSkillId { get; set; }

    public required string Name { get; set; }
    public PlayerClass? ClassRestriction { get; set; }
    public string? Effect { get; set; }
    public string? RequirementNotes { get; set; }
    public string? ImagePath { get; set; }

    public int XpCost { get; set; }
    public int? LevelRequirement { get; set; }

    public Game Game { get; set; } = null!;
    public Skill? Skill { get; set; }

    public ICollection<GameSkillBuildingRequirement> BuildingRequirements { get; set; } = [];
    public ICollection<CraftingSkillRequirement> CraftingRequirements { get; set; } = [];
}
```

Build shared:
```
dotnet build src/OvcinaHra.Shared/OvcinaHra.Shared.csproj
```
Expect errors in places that still reference old shape — those get fixed in later tasks. Don't worry about them yet.

Commit:
```bash
git add src/OvcinaHra.Shared/Domain/Entities/GameSkill.cs
git commit -m "feat(skills): reshape GameSkill as first-class entity [v0.7.0-wip]"
```

---

### Task 1.2: Create `GameSkillBuildingRequirement` join entity

**File:** create `src/OvcinaHra.Shared/Domain/Entities/GameSkillBuildingRequirement.cs`

```csharp
namespace OvcinaHra.Shared.Domain.Entities;

public class GameSkillBuildingRequirement
{
    public int GameSkillId { get; set; }
    public int BuildingId { get; set; }

    public GameSkill GameSkill { get; set; } = null!;
    public Building Building { get; set; } = null!;
}
```

Commit:
```bash
git add src/OvcinaHra.Shared/Domain/Entities/GameSkillBuildingRequirement.cs
git commit -m "feat(skills): add GameSkillBuildingRequirement join entity [v0.7.0-wip]"
```

---

### Task 1.3: Drop `Skill.GameSkills` reverse navigation and reshape `Skill`'s `CraftingRequirements` nav

**File:** modify `src/OvcinaHra.Shared/Domain/Entities/Skill.cs`

Look at the current file. Two changes:
- Keep `BuildingRequirements` (global template's own buildings).
- Keep `GameSkills` — the reverse nav is still valid (each `GameSkill` can optionally point back to its template).
- **Remove** `CraftingRequirements` property — `CraftingSkillRequirement` no longer references `Skill` directly; it references `GameSkill`.

Confirm the entity's nav collections are: `BuildingRequirements`, `GameSkills`. Nothing else.

```csharp
public ICollection<SkillBuildingRequirement> BuildingRequirements { get; set; } = [];
public ICollection<GameSkill> GameSkills { get; set; } = [];
// delete: public ICollection<CraftingSkillRequirement> CraftingRequirements { get; set; } = [];
```

Commit:
```bash
git add src/OvcinaHra.Shared/Domain/Entities/Skill.cs
git commit -m "refactor(skills): Skill no longer owns CraftingRequirements nav [v0.7.0-wip]"
```

---

### Task 1.4: Reshape `CraftingSkillRequirement` to reference `GameSkill`

**File:** modify `src/OvcinaHra.Shared/Domain/Entities/CraftingSkillRequirement.cs`

```csharp
namespace OvcinaHra.Shared.Domain.Entities;

public class CraftingSkillRequirement
{
    public int CraftingRecipeId { get; set; }
    public int GameSkillId { get; set; }

    public CraftingRecipe CraftingRecipe { get; set; } = null!;
    public GameSkill GameSkill { get; set; } = null!;
}
```

Commit:
```bash
git add src/OvcinaHra.Shared/Domain/Entities/CraftingSkillRequirement.cs
git commit -m "refactor(skills): CraftingSkillRequirement now points to GameSkill [v0.7.0-wip]"
```

---

### Task 1.5: Rewrite `GameSkillConfiguration`

**File:** modify `src/OvcinaHra.Api/Data/Configurations/GameSkillConfiguration.cs`

Reference `ItemConfiguration.cs` for the scalar-field conventions (string max lengths, string-converted enum, etc.).

Required mappings:
- `HasKey(x => x.Id)` — surrogate PK now
- `Name` required, max 100
- `ClassRestriction` stored as string, max 20, nullable
- `Effect`, `RequirementNotes` nullable, max 1000 each
- `ImagePath` nullable, max 500
- FK to `Game` (cascade delete from game)
- FK to `Skill` via `TemplateSkillId` — `OnDelete(DeleteBehavior.SetNull)` so deleting a template doesn't kill game copies
- Unique index on `(GameId, Name)` — one name per game
- Unique index on `(GameId, TemplateSkillId)` WHERE TemplateSkillId IS NOT NULL — one copy of each template per game (optional but sensible; the UI picker already filters, so this is a safety net). Use `HasIndex(...).IsUnique().HasFilter("\"TemplateSkillId\" IS NOT NULL")`.
- CHECK constraints:
  - `CK_GameSkill_XpCost_NonNegative`: `"XpCost" >= 0`
  - `CK_GameSkill_LevelRequirement_NonNegative`: `"LevelRequirement" IS NULL OR "LevelRequirement" >= 0`

Commit:
```bash
git add src/OvcinaHra.Api/Data/Configurations/GameSkillConfiguration.cs
git commit -m "feat(skills): update GameSkillConfiguration for first-class entity [v0.7.0-wip]"
```

---

### Task 1.6: Create `GameSkillBuildingRequirementConfiguration`

**File:** create `src/OvcinaHra.Api/Data/Configurations/GameSkillBuildingRequirementConfiguration.cs`

Mirror the `SkillBuildingRequirementConfiguration` pattern:
- `HasKey(x => new { x.GameSkillId, x.BuildingId })`
- FK to GameSkill — cascade from parent
- FK to Building — match how `SkillBuildingRequirement` handles its Building FK (likely cascade or restrict; follow the sibling file)

Commit:
```bash
git add src/OvcinaHra.Api/Data/Configurations/GameSkillBuildingRequirementConfiguration.cs
git commit -m "feat(skills): add GameSkillBuildingRequirement EF Core configuration [v0.7.0-wip]"
```

---

### Task 1.7: Rewrite `CraftingSkillRequirementConfiguration`

**File:** modify `src/OvcinaHra.Api/Data/Configurations/CraftingSkillRequirementConfiguration.cs`

Composite PK `(CraftingRecipeId, GameSkillId)`. FK to `CraftingRecipe` (cascade from recipe). FK to `GameSkill` via `GameSkillId` — **use `DeleteBehavior.Restrict`** so you can't drop a `GameSkill` that's still required by a recipe (the endpoint layer returns 409 first; this is a DB safety net).

Commit:
```bash
git add src/OvcinaHra.Api/Data/Configurations/CraftingSkillRequirementConfiguration.cs
git commit -m "refactor(skills): CraftingSkillRequirement config points to GameSkill [v0.7.0-wip]"
```

---

### Task 1.8: Update `WorldDbContext` DbSets

**File:** modify `src/OvcinaHra.Api/Data/WorldDbContext.cs`

- Keep `DbSet<GameSkill> GameSkills`.
- Keep `DbSet<CraftingSkillRequirement> CraftingSkillRequirements`.
- Add `DbSet<GameSkillBuildingRequirement> GameSkillBuildingRequirements => Set<GameSkillBuildingRequirement>();`.

Build API project:
```
dotnet build src/OvcinaHra.Api/OvcinaHra.Api.csproj
```
Likely fails in endpoints that still reference old shapes. That's fine — we fix endpoints in Phase 2. For this task, confirm the entities/configs/DbSets are at least consistent.

Commit:
```bash
git add src/OvcinaHra.Api/Data/WorldDbContext.cs
git commit -m "feat(skills): register GameSkillBuildingRequirements DbSet [v0.7.0-wip]"
```

---

### Task 1.9: Delete the old migration and generate a fresh one

**Files:**
- Delete: `src/OvcinaHra.Api/Migrations/20260419045332_AddSkillsDomain.cs`
- Delete: `src/OvcinaHra.Api/Migrations/20260419045332_AddSkillsDomain.Designer.cs`
- Modify (revert to pre-skills shape): `src/OvcinaHra.Api/Migrations/WorldDbContextModelSnapshot.cs`

Step 1:
```bash
git rm src/OvcinaHra.Api/Migrations/20260419045332_AddSkillsDomain.cs src/OvcinaHra.Api/Migrations/20260419045332_AddSkillsDomain.Designer.cs
```

Step 2: regenerate the model snapshot. Simplest approach — generate a NEW migration that represents the desired state, let EF compute the diff from a clean snapshot. But because we deleted the old migration, we need the snapshot to match a state WITHOUT those tables first. Easiest hack: manually remove the skill-domain entries from `WorldDbContextModelSnapshot.cs` (search for `Skills`, `GameSkills`, `SkillBuildingRequirements`, `CraftingSkillRequirements`, `GameSkillBuildingRequirements` and remove those blocks).

Alternative: drop the dev DB, remove the snapshot file entirely, and let `dotnet ef migrations add` regenerate from the current model. **Use this alternative — less error-prone.**

Commands:
```bash
# From repo root
rm src/OvcinaHra.Api/Migrations/WorldDbContextModelSnapshot.cs
cd src/OvcinaHra.Api
# Delete dev DB so there's no drift
docker compose -f ../../docker-compose.yml exec postgres psql -U postgres -c "DROP DATABASE IF EXISTS ovcinahra;"
# Regenerate migration from scratch — EF will produce a snapshot matching full current model
dotnet ef migrations add FullInitialSchema
```

Wait — generating a migration called "FullInitialSchema" that includes EVERY table in the project is a lot. That replaces all the historical migrations. We don't want that.

Revised approach: keep all migrations except the skills-domain one, regenerate a FRESH skills migration that the EF diff engine computes from the post-snapshot-edit state.

**Actually the cleanest realistic approach:**

1. Find the last migration BEFORE `AddSkillsDomain` (look at `src/OvcinaHra.Api/Migrations/` — sort by timestamp).
2. Restore model snapshot to that migration's snapshot state. You can do this by looking at that migration's `.Designer.cs` file — its `BuildTargetModel` method is the snapshot at that time.
3. Copy that `BuildTargetModel` implementation into `WorldDbContextModelSnapshot.cs` (without changing the file otherwise).
4. Run `dotnet ef migrations add AddSkillsDomain` — EF diffs against the restored snapshot and produces a migration covering all skill tables + the new CraftingSkillRequirement column rename.

This sounds finicky. **If the implementer is not confident, ASK for guidance or stop and flag.** Do not guess.

Step 3: apply the new migration to a dropped-and-recreated dev DB.
```bash
cd <worktree-root>
docker compose up -d postgres
sleep 3
# Drop the ovcinahra database (Postgres user/password from docker-compose.yml)
docker compose exec postgres psql -U postgres -c "DROP DATABASE IF EXISTS ovcinahra;"
docker compose exec postgres psql -U postgres -c "CREATE DATABASE ovcinahra;"
cd src/OvcinaHra.Api
dotnet ef database update
```

Expected: all tables created cleanly.

Commit the new migration files:
```bash
git add src/OvcinaHra.Api/Migrations/
git commit -m "feat(skills): EF Core migration for copy-on-assign schema [v0.7.0-wip]"
```

---

## Phase 2 — API

### Task 2.1: Update `GameSkillDto` + new request DTOs

**File:** modify `src/OvcinaHra.Shared/Dtos/GameDtos.cs`

Replace the existing `GameSkillDto` and `UpsertGameSkillRequest` with:

```csharp
public record GameSkillDto(
    int Id,
    int GameId,
    int? TemplateSkillId,
    string Name,
    PlayerClass? ClassRestriction,
    string? Effect,
    string? RequirementNotes,
    string? ImagePath,
    int XpCost,
    int? LevelRequirement,
    IReadOnlyList<int> BuildingRequirementIds);

public record CreateGameSkillRequest(
    int? TemplateSkillId,
    string Name,
    PlayerClass? ClassRestriction,
    string? Effect,
    string? RequirementNotes,
    IReadOnlyList<int> BuildingRequirementIds,
    int XpCost,
    int? LevelRequirement);

public record UpdateGameSkillRequest(
    string Name,
    PlayerClass? ClassRestriction,
    string? Effect,
    string? RequirementNotes,
    IReadOnlyList<int> BuildingRequirementIds,
    int XpCost,
    int? LevelRequirement);
```

Remove `UpsertGameSkillRequest` (no longer used — we have distinct create and update shapes now).

Build shared:
```
dotnet build src/OvcinaHra.Shared/OvcinaHra.Shared.csproj
```

Commit:
```bash
git add src/OvcinaHra.Shared/Dtos/GameDtos.cs
git commit -m "feat(skills): update GameSkill DTOs for first-class entity [v0.7.0-wip]"
```

---

### Task 2.2: Rewrite per-game skill endpoints in `GameEndpoints.cs`

**File:** modify `src/OvcinaHra.Api/Endpoints/GameEndpoints.cs`

Find and replace the current three handlers (GET list, PUT upsert, DELETE) with FOUR handlers:

1. `GET /api/games/{gameId:int}/skills` — list `GameSkillDto[]` for the game. Include `BuildingRequirements` so `BuildingRequirementIds` is populated.

2. `GET /api/games/{gameId:int}/skills/{gameSkillId:int}` — single GameSkillDto. 404 if not found or gameSkill.GameId != gameId.

3. `POST /api/games/{gameId:int}/skills` — create. Body `CreateGameSkillRequest`. If `TemplateSkillId` supplied, validate it exists in Skills table (400 if not). Validate `BuildingRequirementIds` all exist (400 if any missing). Validate Name is non-empty (400). Validate unique name in game (409). Create `GameSkill` row + `GameSkillBuildingRequirement` rows. Single SaveChangesAsync. Return 201 with the persisted DTO.

4. `PUT /api/games/{gameId:int}/skills/{gameSkillId:int}` — update. Body `UpdateGameSkillRequest`. 404 if gameSkill missing. Validate building ids. Validate unique name in game excluding self. Update scalars + replace building-requirements set. Single SaveChangesAsync. Return 204.

5. `DELETE /api/games/{gameId:int}/skills/{gameSkillId:int}` — delete. 404 if missing. Check `CraftingSkillRequirements.AnyAsync(csr => csr.GameSkillId == gameSkillId)` → 409 with Czech "Nelze odebrat dovednost — je vyžadována alespoň jedním receptem v této hře." Otherwise remove + SaveChangesAsync. Return 204.

Keep the Czech error messages matching what's already in the codebase for consistency (reuse where possible).

Commit:
```bash
git add src/OvcinaHra.Api/Endpoints/GameEndpoints.cs
git commit -m "feat(skills): reshape per-game skill endpoints for first-class entity [v0.7.0-wip]"
```

---

### Task 2.3: Update recipe endpoint semantics in `CraftingEndpoints.cs`

**File:** modify `src/OvcinaHra.Api/Endpoints/CraftingEndpoints.cs`

Current behavior: `RequiredSkillIds` in request payload = `Skill.Id`s (catalog templates). Validation checks (a) skill exists globally, (b) skill has a GameSkill row for this game.

New behavior: `RequiredSkillIds` = `GameSkill.Id`s directly. Validation is simpler:
- Each id must be a `GameSkill` AND its `GameId` must equal the recipe's `GameId`.

Rewrite `ValidateRequiredSkillsAsync` (or whatever the private helper is called) to:

```csharp
private static async Task<ProblemDetails?> ValidateRequiredSkillsAsync(
    WorldDbContext db, int gameId, IReadOnlyList<int> gameSkillIds, CancellationToken ct)
{
    if (gameSkillIds.Count == 0) return null;

    var validIds = await db.GameSkills
        .Where(gs => gs.GameId == gameId && gameSkillIds.Contains(gs.Id))
        .Select(gs => gs.Id)
        .ToArrayAsync(ct);

    var missing = gameSkillIds.Except(validIds).ToArray();
    if (missing.Length > 0)
    {
        return new ProblemDetails
        {
            Title = "Některé požadované dovednosti v této hře neexistují.",
            Detail = $"ID dovedností [{string.Join(", ", missing)}] nejsou ve hře dostupné.",
            Status = StatusCodes.Status400BadRequest
        };
    }

    return null;
}
```

Update the POST and PUT recipe handlers to pass GameSkillIds (the incoming list) — code changes are minimal.

`CraftingSkillRequirement` insertion: set `GameSkillId` (not `SkillId`) when adding rows.

Commit:
```bash
git add src/OvcinaHra.Api/Endpoints/CraftingEndpoints.cs
git commit -m "refactor(skills): recipe endpoints require GameSkill IDs [v0.7.0-wip]"
```

---

### Task 2.4: Rewrite `GameSkillEndpointsTests.cs`

**File:** rewrite `tests/OvcinaHra.Api.Tests/Endpoints/GameSkillEndpointsTests.cs`

Replace the existing 8 tests with tests that exercise the new contract:

1. `Post_AddsFromTemplate_CopiesAllFields` — seed template with specific fields; POST with `TemplateSkillId` + same field values; assert persisted GameSkill matches.
2. `Post_CreatesCustom_NoTemplate_AcceptsNullTemplateId` — POST with `TemplateSkillId = null`; assert created with null.
3. `Post_DuplicateNameInSameGame_Returns409` — create once, POST again same name → 409.
4. `Post_NegativeXpCost_Returns400`.
5. `Post_NegativeLevelRequirement_Returns400`.
6. `Post_LevelRequirementNull_Accepted`.
7. `Put_UpdatesAllFields` — create, PUT with new values, assert persisted.
8. `Put_ChangingNameToAnotherGameSkillsName_Returns409`.
9. `Put_UnknownBuildingId_Returns400`.
10. `Get_ListsOnlySkillsInGame` — two games, skills in each; GET for one returns only its own.
11. `Delete_RemovesGameSkill`.
12. `Delete_BlockedWhenRecipeReferences_Returns409`.
13. `DeletingTemplate_NullsOutTemplateSkillIdOnCopies` — the OnDelete(SetNull) cascade test.

Use the same `IntegrationTestBase` + `PostgresFixture` pattern the other tests use. Seed via direct DbContext access where needed.

After writing tests, run:
```
dotnet test tests/OvcinaHra.Api.Tests/ --filter "FullyQualifiedName~GameSkillEndpointsTests"
```
Expected: all pass. Fix bugs in the handlers if any test fails.

Commit:
```bash
git add tests/OvcinaHra.Api.Tests/Endpoints/GameSkillEndpointsTests.cs
git commit -m "test(skills): rewrite GameSkill endpoint tests for copy-on-assign shape [v0.7.0-wip]"
```

---

### Task 2.5: Update crafting endpoint tests

**File:** modify `tests/OvcinaHra.Api.Tests/Endpoints/CraftingEndpointTests.cs`

The 4 skill-related tests (`CreateRecipe_WithRequiredSkillIds_PersistsLinks`, `UpdateRecipe_ReplacesSkillSet`, `CreateRecipe_SkillNotInGame_Returns400`, `CreateRecipe_UnknownSkillId_Returns400`) have seeds that create skills via the template path and pass template IDs to the recipe. These need updating:

- Seeds must create GameSkill rows (not just Skill rows + GameSkill rows — just create GameSkill rows directly with DbContext).
- `RequiredSkillIds` in the recipe request = GameSkill.Id (not template id).
- For the "not in game" 400 test: create a GameSkill in game A, attempt recipe in game B with that GameSkill's id → expect 400.
- For the "unknown id" 400 test: use an int far outside seeded ids.

Run the full crafting test suite:
```
dotnet test tests/OvcinaHra.Api.Tests/ --filter "FullyQualifiedName~CraftingEndpointTests"
```
Expected: all 13 pass.

Commit:
```bash
git add tests/OvcinaHra.Api.Tests/Endpoints/CraftingEndpointTests.cs
git commit -m "test(skills): adjust crafting tests for GameSkill ID references [v0.7.0-wip]"
```

---

### Task 2.6: Update `SkillEndpointsTests.cs` to match new behavior

**File:** modify `tests/OvcinaHra.Api.Tests/Endpoints/SkillEndpointsTests.cs`

Two changes:
- The `Delete_WithGameSkillReference_Returns409` test is no longer valid — template deletion is now permitted even with references (cascade nulls `TemplateSkillId`). Replace it with `Delete_WithGameSkillReference_NullsOutTemplateSkillId`:
  - Create template, create a GameSkill referencing it, DELETE the template, verify the GameSkill row still exists and its `TemplateSkillId` is now null.
- The `Delete_WithRecipeReference_Returns409` test is also no longer valid — recipes now reference GameSkill, not Skill. Remove that test.

Run:
```
dotnet test tests/OvcinaHra.Api.Tests/ --filter "FullyQualifiedName~SkillEndpointsTests&FullyQualifiedName!~GameSkillEndpointsTests"
```
Expected: 15 pass (was 16, minus the removed recipe-reference test, plus the new cascade test).

Commit:
```bash
git add tests/OvcinaHra.Api.Tests/Endpoints/SkillEndpointsTests.cs
git commit -m "test(skills): update template-delete tests for cascade-null behavior [v0.7.0-wip]"
```

---

### Task 2.7: Run the entire API test suite end-to-end

```
dotnet test tests/OvcinaHra.Api.Tests/
```
Expected: all tests pass. If anything doesn't, fix in a small follow-up commit and retry.

No separate commit unless fixes are needed.

---

## Phase 3 — Client

### Task 3.1: Update `SkillService` client wrapper

**File:** modify `src/OvcinaHra.Client/Services/SkillService.cs`

- `GetGameSkillsAsync(gameId)` stays; return type is `IReadOnlyList<GameSkillDto>` (DTO shape changed, no method signature change).
- Replace `UpsertGameSkillAsync(gameId, skillId, UpsertGameSkillRequest)` with:
  - `CreateGameSkillAsync(gameId, CreateGameSkillRequest)` → returns `GameSkillDto`
  - `UpdateGameSkillAsync(gameId, gameSkillId, UpdateGameSkillRequest)` → returns `Task` (204)
- `RemoveGameSkillAsync(gameId, gameSkillId)` stays; the route parameter is now `gameSkillId` instead of `skillId` (API side enforces).

Commit:
```bash
git add src/OvcinaHra.Client/Services/SkillService.cs
git commit -m "feat(skills): SkillService matches new GameSkill first-class endpoints [v0.7.0-wip]"
```

---

### Task 3.2: Refresh `/skills` catalog page with info banner

**File:** modify `src/OvcinaHra.Client/Pages/Skills/Skills.razor`

Minimal changes:
- Add an info banner just below the page header: `<div class="alert alert-info small">Tyto dovednosti jsou šablony. Při přidání do hry se vytvoří kopie, kterou lze pro danou hru upravit nezávisle.</div>`
- Ensure the page title says "Knihovna dovedností" (title text, not the browser title — update the `<h1>`).
- Remove any reference to XP/Level fields from the detail popup (if any remain from earlier iterations — there shouldn't be, but verify).

Build client:
```
dotnet build src/OvcinaHra.Client/OvcinaHra.Client.csproj
```
Expected: 0 errors.

Commit:
```bash
git add src/OvcinaHra.Client/Pages/Skills/Skills.razor
git commit -m "feat(skills): relabel /skills as Knihovna dovedností + info banner [v0.7.0-wip]"
```

---

### Task 3.3: Rewrite `/games/{gameId}/skills` for richer editing + chooser flow

**File:** modify `src/OvcinaHra.Client/Pages/Games/GameSkills.razor`

Biggest UI change in this plan. Structure:

- Grid of `GameSkillDto` rows: Název, Povolání, XP, Úroveň, action column (edit, [×] remove).
- Row click → **edit popup** with ALL fields editable: Název (DxTextBox), Typ (class/Dobrodruh radio + DxComboBox), Efekt (DxMemo), Poznámka (DxMemo), Požadované budovy (add/remove list), XP (DxSpinEdit), Úroveň (DxSpinEdit + "Bez požadavku" toggle). Above the form, small info line: "Šablona: {templateName} (Knihovna)" OR "Vlastní (bez šablony)" OR "Šablona: (odstraněná)" based on TemplateSkillId state.
- `+ Přidat dovednost` opens a small **chooser popup** with two buttons:
  - **Ze šablony** → on click, close chooser, open a picker popup: `DxComboBox` of templates not already in this game (`await SkillSvc.GetAllAsync()` filtered by current GameSkill SkillIds). User picks → close picker, open the main edit popup pre-filled with the template's fields + empty XP/Level → user adjusts → save creates a GameSkill.
  - **Vlastní** → on click, close chooser, open the main edit popup EMPTY, TemplateSkillId=null → save creates GameSkill.
- Save button in the main popup always calls `CreateGameSkillAsync` or `UpdateGameSkillAsync` depending on mode.
- Delete button removes from game via `RemoveGameSkillAsync`. Handle 409 with Czech error banner in the popup.

Reuse Czech strings from the existing `Skills.razor` where they fit; reinvent sparingly.

Build:
```
dotnet build src/OvcinaHra.Client/OvcinaHra.Client.csproj
```

Commit:
```bash
git add src/OvcinaHra.Client/Pages/Games/GameSkills.razor
git commit -m "feat(skills): richer per-game skill page with template chooser and full edit [v0.7.0-wip]"
```

---

### Task 3.4: Make NavMenu Hra-section "Dovednosti" dynamic

**File:** modify `src/OvcinaHra.Client/Layout/NavMenu.razor`

- Inject `GameContextService GameContext` at the top if not already.
- Find the two "Dovednosti" entries (earlier investigation: lines ~54 Hra section, ~116 Katalog section).
- **Hra section:**
  - Wrap the link in `@if (GameContext.SelectedGameId is int gid)` so it only renders when there's an active game.
  - Update href to use `@($"/games/{gid}/skills")` (Blazor interpolation).
- **Katalog section:** leave as `href="skills"` (unchanged).
- Add an HTML comment near both entries:
  ```razor
  @* Skills are copy-on-assign: Hra link goes to per-game copies, Katalog link goes to templates. Intentional asymmetry with Items. *@
  ```

Build:
```
dotnet build src/OvcinaHra.Client/OvcinaHra.Client.csproj
```

Commit:
```bash
git add src/OvcinaHra.Client/Layout/NavMenu.razor
git commit -m "feat(skills): NavMenu Hra link dynamic per active game [v0.7.0-wip]"
```

---

### Task 3.5: Recipe dialog sanity check

**File:** `src/OvcinaHra.Client/Pages/Items/ItemList.razor`

The "Požadované dovednosti" block already uses `SkillSvc.GetGameSkillsAsync(gameId)` to fetch the dropdown and puts `gs.SkillId`... wait — in the old model `GameSkillDto.SkillId` was a template id; in the new model the DTO has `.Id` (surrogate PK) instead. The recipe dialog needs to pass GameSkill.Id (not template id) into `RequiredSkillIds` now.

Grep the file for `SkillId` references in this block and change to `.Id`. Specifically: the picker's `@bind-Value` target, the `newSkillId`/`selectedSkillToAdd` fields, and anywhere `gs.SkillId` is used — all should become `gs.Id`.

Build + manually exercise in smoke test (Task 4).

Commit:
```bash
git add src/OvcinaHra.Client/Pages/Items/ItemList.razor
git commit -m "fix(imagepicker/recipe): recipe dialog uses GameSkill.Id now [v0.7.0-wip]"
```

(commit scope labeled imagepicker/recipe because ItemList is shared — if the grep only finds the skills block, adjust commit message to `fix(skills-recipe): ...`.)

---

## Phase 4 — Manual smoke test + version bump

### Task 4.1: Smoke test (user turn, not subagent)

Run `devStart.bat`, exercise:

1. `/skills` — info banner visible, edit a skill, save.
2. `/games/{id}/skills` — grid columns XP + Úroveň visible.
3. Add "Ze šablony" — pick a template, fill XP, save. Row appears.
4. Edit existing GameSkill — change Name, Effect. Save. Verify `/skills` (template) is NOT changed. ✔ Copy semantics.
5. Go to `/skills`, edit the template's Effect → verify the GameSkill copy is unchanged.
6. Add "Vlastní" → fill everything → save. Grid shows it with "Vlastní (bez šablony)" on open.
7. Delete the template that a GameSkill references → GameSkill row still there, popup info line says "Šablona: (odstraněná)".
8. Open Item edit dialog → require GameSkill in recipe → save → reload → still there.
9. Try to remove GameSkill from game while required by a recipe → 409 Czech message.
10. NavMenu Hra → Dovednosti → `/games/{activeGameId}/skills`. No active game → link hidden.
11. NavMenu Katalog světa → Dovednosti → `/skills`.

Report any UI issue back; fixes go into the appropriate phase 3 file.

---

### Task 4.2: Version bump and PR

**File:** modify `src/OvcinaHra.Client/OvcinaHra.Client.csproj`

- Bump `<Version>0.6.0</Version>` → `<Version>0.7.0</Version>`.

Commit:
```bash
git add src/OvcinaHra.Client/OvcinaHra.Client.csproj
git commit -m "chore: bump client version to 0.7.0 for skills copy-on-assign [v0.7.0]"
```

Push and open PR:
```bash
git push -u origin feat/skills-copy-on-assign
gh pr create --base main --title "feat(skills): copy-on-assign domain + UI split refactor [v0.7.0]" --body "..."
```

PR body should summarize:
- Domain shift: GameSkill is now a standalone editable copy
- Migration: replaces the prior AddSkillsDomain migration (safe — no prod data)
- API contract changes: GameSkillDto shape, POST creates, PUT updates, `RequiredSkillIds` now = GameSkill.Id
- UI: NavMenu Hra→Dovednosti dynamic via GameContext; per-game page gets full editing + chooser; catalog page gets info banner
- Testing: 15 skill + ~13 game-skill + 13 crafting API tests all green

---

## Completion checklist

- [ ] Phase 1 — entities, configs, migration all committed; migration applies cleanly to fresh dev DB
- [ ] Phase 2 — all endpoints reshaped; full API test suite green
- [ ] Phase 3 — client service + pages + nav updated
- [ ] Phase 4 — manual smoke passes on all 11 scenarios
- [ ] `dotnet build OvcinaHra.slnx` — 0 errors
- [ ] No `UpsertGameSkillRequest`, no `SkillId` usage in recipe DTOs, no `Field="GameSkill.SkillId"` in razor files: `grep -rn "UpsertGameSkillRequest\|GameSkill\.SkillId" src/ --include="*.cs" --include="*.razor"` — zero
- [ ] Version bumped to 0.7.0
- [ ] PR opened

## Notes for implementer

- The Phase 1 migration regeneration (Task 1.9) is the riskiest step. If EF Core snapshot restoration doesn't work cleanly, stop and flag rather than fighting it. Alternative fallback: manually author the migration SQL by hand. But try the clean approach first.
- Phase 2 tests assume `IntegrationTestBase` and `PostgresFixture` work unchanged — they should, since the infrastructure tests and fixtures are orthogonal to the Skill domain.
- Czech strings should be reused from the existing `Skills.razor` and `GameSkills.razor` where they fit. When inventing new ones (e.g., "Šablona: (odstraněná)"), keep short.
- The recipe dialog change (Task 3.5) is small but easy to miss — the `SkillId` → `.Id` substitution is silent if you don't grep for it.
- If anything in the existing codebase contradicts the design doc, trust the design doc and flag the contradiction.
