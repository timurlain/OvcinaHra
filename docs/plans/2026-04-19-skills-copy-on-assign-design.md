# Skills Copy-on-Assign — Design

**Date:** 2026-04-19
**Status:** Approved — ready for implementation plan
**Supersedes:** `docs/plans/2026-04-19-skills-page-pattern-align-design.md` (same branch, scoped only to UI alignment — rendered obsolete after this broader decision). The original skills-domain design (`docs/plans/2026-04-19-skills-domain-design.md`) also no longer reflects reality; it should get a pointer to this document after merge.

## Goal

Make each game's `GameSkill` a **standalone editable copy** of a catalog `Skill` template, not a pure reference. An organizer can freely customize the Effect text, Name, Building requirements, XP cost and level requirement for the skill as it appears in their specific game, without that edit rippling to other games or back to the library template.

## Why (Copilot review triggered a deeper rethink)

PR #34 review flagged the NavMenu as having two "Dovednosti" links that both point at the global catalog — a pattern inconsistency with Items/Monsters (which use a single `?catalog=true`-toggled page). Investigating why Skills were split revealed the real issue: the data model treats `GameSkill` as a pure pointer to a global `Skill`, so editing the catalog's Effect ripples to every game using that skill. That doesn't match how organizers think ("I want a slightly different Effect wording for this particular game"). Rather than paper over the inconsistency, this design embraces the split and makes it meaningful — **catalog = library of templates, game-skill = the game's own editable copy.**

## Non-goals

- **No backward-compat fuss.** No production data yet, so the migration can be a clean replace of the current `AddSkillsDomain` migration file rather than a multi-step transform.
- Skills image upload (endpoint still missing for `"skills"` entity type) — separate future ticket.
- Template versioning / "update all copies from template" button — not asked for; YAGNI.

## Data model

### Entities

**`Skill`** — the catalog template. **Unchanged.**
- `Id`, `Name`, `ClassRestriction: PlayerClass?`, `Effect?`, `RequirementNotes?`, `ImagePath?`, `BuildingRequirements` (join to `Building`).

**`GameSkill`** — now a first-class entity, the per-game copy.
- `Id` (surrogate PK — NEW)
- `GameId` (FK)
- `TemplateSkillId` (FK to `Skill`, nullable — `null` when created via "Vlastní" flow)
- `Name`, `ClassRestriction`, `Effect`, `RequirementNotes`, `ImagePath` (copied from template on assign; independently editable afterward)
- `XpCost: int`, `LevelRequirement: int?`
- Navigation: `Game`, `Skill` (template), `BuildingRequirements` (new join)

**`GameSkillBuildingRequirement`** — new join table
- Composite PK `(GameSkillId, BuildingId)`

**`CraftingSkillRequirement`** — migrates its reference
- `CraftingRecipeId`, **`GameSkillId`** (renamed from `SkillId`; now points to per-game `GameSkill`, not to global `Skill`)
- The "skill must be in the game" constraint is now a plain FK + the recipe's game matching the GameSkill's game — enforced server-side at recipe save.

### Constraints

- `GameSkill (GameId, Name)` — unique. One game cannot have two skills with the same name.
- `GameSkill.XpCost >= 0` — CHECK, same as today.
- `GameSkill.LevelRequirement IS NULL OR >= 0` — CHECK, same.
- `Skill.OnDelete` → `SetNull` on `GameSkill.TemplateSkillId`. Template can be deleted even while copies reference it; the copies just lose their provenance pointer.
- `CraftingRecipe.OnDelete` → cascade to `CraftingSkillRequirements`.
- `GameSkill.OnDelete` → restrict. If any `CraftingSkillRequirement` points at it, deletion returns 409 (handled at endpoint level with a Czech message).

## Migration

Single replacement migration. No backfill SQL because no production data.

1. **Delete** the existing `src/OvcinaHra.Api/Migrations/20260419045332_AddSkillsDomain.cs` (+ Designer file).
2. **Regenerate** a fresh migration `AddSkillsDomain` that defines the final shape: `Skills`, `SkillBuildingRequirements` (unchanged), new-shape `GameSkills` (first-class entity), new `GameSkillBuildingRequirements`, new-shape `CraftingSkillRequirements` (pointing to `GameSkillId`).
3. Dev DB reset: `dotnet ef database drop --force && dotnet ef database update`.

This is clean precisely because there is no prod data to preserve. The migration is a single self-contained snapshot of the final schema.

## API

### Catalog template endpoints — unchanged

- `GET /api/skills`, `GET /api/skills/{id}`, `POST /api/skills`, `PUT /api/skills/{id}`, `DELETE /api/skills/{id}`.
- `DELETE` is permitted even if copies reference the template (FK uses `SetNull`).

### Per-game skill endpoints — reshaped

| Method | Route | Purpose |
|---|---|---|
| GET | `/api/games/{gameId}/skills` | list all GameSkills for the game |
| GET | `/api/games/{gameId}/skills/{gameSkillId}` | single GameSkill detail |
| POST | `/api/games/{gameId}/skills` | add from template OR create custom. Body has optional `TemplateSkillId` (null = custom) + all full fields. Server validates FK + uniqueness; request body wins on all values. |
| PUT | `/api/games/{gameId}/skills/{gameSkillId}` | update all fields (Name, Class, Effect, Notes, BuildingIds, XpCost, Level) |
| DELETE | `/api/games/{gameId}/skills/{gameSkillId}` | remove from game; 409 if a recipe in this game requires it |

### Recipe endpoints

- `CraftingRecipeDto.RequiredSkillIds` is now **`IReadOnlyList<int>` of `GameSkill.Id`s**, not template `Skill.Id`s. Callers adjust.
- Validation on recipe save: each id must be a `GameSkill` with matching `GameId`. Simpler than before — no existence + in-game membership check; just a filtered FK.

### DTOs

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

## UI

### `/skills` — "Knihovna dovedností" (Library)

- Grid: Název, Povolání, Efekt, Požadované budovy, Poznámka.
- Detail popup: Name, Typ (class/Dobrodruh toggle), Effect, Notes, Building list, Image (stub).
- **No XP/Level fields anywhere** — those belong to GameSkill.
- Info banner at top: *"Tyto dovednosti jsou šablony. Při přidání do hry se vytvoří kopie, kterou lze pro danou hru upravit nezávisle."*
- Create / edit / delete — all affect the template only.

### `/games/{gameId}/skills` — "Dovednosti ve hře"

- Grid of `GameSkillDto`. Columns: Název, Povolání, XP, Úroveň, action column with row delete.
- Row click → detail popup with **all fields editable** (Name, Class, Effect, Notes, Buildings, XP, Level). Above the form, a small info line: *"Šablona: Tichý úder (Knihovna)"* or *"Vlastní (bez šablony)"* or *"Šablona: (odstraněná)"* if TemplateSkillId became null after template deletion.
- Add button "+ Přidat dovednost" opens a small chooser popup:
  - **Ze šablony** → `DxComboBox` of templates not already in game → confirm → opens full edit popup pre-filled with template values → organizer tweaks XP/Level + any field → save creates the GameSkill.
  - **Vlastní** → opens full edit popup empty, no template → organizer fills in everything → save creates GameSkill with `TemplateSkillId = null`.
- Row delete = remove-from-game; 409 blocked if a recipe in this game requires the skill.

### NavMenu

- **Hra → Dovednosti** — `href` computed at render time from `GameContextService.SelectedGameId`:
  - Active game: `/games/{gameId}/skills`
  - No active game: disabled link OR navigates to a small "Žádná aktivní hra — Otevřít knihovnu" page. Simplest is to hide the link when no active game.
- **Katalog světa → Dovednosti** — `/skills` (template library).
- A one-line HTML comment next to both entries explains why these point to different resources:
  ```html
  <!-- Skills are copy-on-assign: catalog = templates, per-game = editable copies. Intentional asymmetry with Items. -->
  ```

### Recipe editor

- "Požadované dovednosti" block in the item-edit dialog already queries `GameSkillDto` for the current game. After the data-model change the DTO is still there and the endpoint is still there; the id it writes into `RequiredSkillIds` is now a GameSkill.Id instead of a template Skill.Id. No UI change needed in the crafting dialog — it just works with the new meaning.

## Testing

**API integration tests** (Testcontainers):

- `SkillEndpointsTests` (existing 16) — mostly pass as-is (template CRUD unchanged); drop the test that asserted "delete blocked when a game references" (that rule is gone), adjust any seed helpers that assumed old GameSkill shape.
- `GameSkillEndpointsTests` — rewrite for new shape:
  - `Post_AddsFromTemplate_CopiesAllFields` (required)
  - `Post_CreatesCustom_NoTemplate` (required)
  - `Put_UpdatesGameSkillFields_TemplateUnaffected` — key isolation test
  - `DeleteTemplate_NullsOutTemplateSkillIdOnCopies` — cascade-null behavior
  - `Delete_GameSkill_BlockedWhenRecipeRequires_Returns409`
  - `Post_DuplicateNameInSameGame_Returns409`
  - Validation: XpCost / LevelRequirement non-negative.
- `CraftingEndpointTests` — the 4 skill-related tests get updated seeds (recipe `RequiredSkillIds` now = GameSkill IDs, not template IDs). Logic-level assertions stay; only fixture setup changes.

**E2E Playwright** — `SkillsManagementTests.cs` updates seed to create template → assign to game → require in recipe using the new endpoints.

**Manual smoke:**
1. Create template in `/skills` → open game → add from template → edit GameSkill's Effect → reload → verify template's Effect is unchanged (copy semantics).
2. Edit template's Effect in `/skills` → verify existing GameSkill copies are unaffected.
3. Create "Vlastní" skill in game (no template) → `TemplateSkillId = null`.
4. Delete template while a GameSkill references it → GameSkill stays, its `TemplateSkillId` becomes null, info line shows "Šablona: (odstraněná)".
5. Require GameSkill in recipe → remove from game → 409.
6. NavMenu from Hra section → lands on `/games/{activeGameId}/skills`. From Katalog světa → lands on `/skills`.

## Versioning & rollout

- Version bump to `0.6.0` — domain/API shape changed (breaking at the contract level). Minor bump, not patch.
- Single PR from branch `feat/skills-page-align-with-catalog-pattern` (branch may want a rename to `feat/skills-copy-on-assign` to reflect the real scope — implementation plan decides).
- Post-merge: add a superseded-by note at the top of `docs/plans/2026-04-19-skills-domain-design.md` pointing here.

## Open questions

None — all six design sections locked during brainstorming.
