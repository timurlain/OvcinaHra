# Skills Domain — Design

**Date:** 2026-04-19
**Status:** Approved — ready for implementation plan
**Trigger:** Crafting recipes need a "required skill" slot (shown in item edit dialog), but the Skill concept does not yet exist in the domain.

## Goal

Introduce **Skill** as a first-class catalog concept in OvcinaHra, alongside Items, Monsters, Buildings, etc., and wire it into the crafting recipe editor so a recipe can require 1..N skills.

Skills represent abilities Hrdinové acquire during a game at XP cost. Two kinds exist:

- **Class skill** — locked to exactly one `PlayerClass`; only a Hrdina of that class can learn it.
- **Adventurer skill ("Dobrodruh")** — no class restriction; any Hrdina can learn it.

Both kinds share the same entity shape; the kind is derived from whether the class restriction is set.

## Non-goals

- Per-Hrdina skill progression / ledger (not in this scope — this is organizer-only CRUD).
- Runtime validation that a Hrdina meets XP/level/class requirements when crafting.
- Skill trees, prerequisites between skills, upgrade paths.

## Data model

All entities live in `src/OvcinaHra.Shared/Domain/Entities/`. Follows existing patterns (Item/GameItem, CraftingRecipe/CraftingBuildingRequirement).

```
Skill                          // global catalog (like Item)
├─ Id (PK)
├─ Name                        // Czech display name, e.g. "Tichý úder"
├─ ClassRestriction: PlayerClass?   // null = adventurer skill ("Dobrodruh"); set = class skill
├─ Effect: string?             // Czech flavor/mechanical effect text
├─ RequirementNotes: string?   // free-form notes about why these requirements exist
├─ ImagePath: string?          // optional, matches Item pattern
└─ nav: BuildingRequirements, GameSkills, CraftingRequirements

SkillBuildingRequirement       // join: Skill ↔ Building
├─ SkillId (PK, FK)            // composite PK, no surrogate
└─ BuildingId (PK, FK)

GameSkill                      // per-game inclusion + knobs (like GameItem)
├─ GameId (PK, FK)             // composite PK
├─ SkillId (PK, FK)
├─ XpCost: int                 // required once skill is in game; CHECK >= 0
└─ LevelRequirement: int?      // nullable; null or 0 = no gate; CHECK >= 0 when not null

CraftingSkillRequirement       // join: CraftingRecipe ↔ Skill
├─ CraftingRecipeId (PK, FK)   // composite PK
└─ SkillId (PK, FK)
```

**Shape rationale:**

- `CraftingSkillRequirement` references `Skill` (global), not `GameSkill` — matches how `CraftingBuildingRequirement.BuildingId` and `CraftingIngredient.ItemId` reference global entities. Per-game availability is enforced at the endpoint level (see below).
- `XpCost` and `LevelRequirement` live on `GameSkill` (per-game tunable), not on `Skill` — matches the Item/GameItem split where game-specific numbers (price, stock) live on the per-game entity.
- `RequirementNotes` is on `Skill` (single text explaining the skill's prerequisites holistically), not on the building-requirement join table.
- `PlayerClass?` as nullable enum means class restriction is the only concept; no parallel "SkillKind" enum. Type is `ClassSkill` iff `ClassRestriction != null`.

**Owned types / value objects:** none. All four entities are plain aggregates with string-converted enums.

**Invariants:**

- `GameSkill.XpCost >= 0` — CHECK constraint + validation.
- `GameSkill.LevelRequirement IS NULL OR LevelRequirement >= 0` — CHECK + validation.
- A recipe's required skill must also exist in that recipe's game (i.e., a `GameSkill` row exists for `recipe.GameId, skillId`) — enforced at endpoint, not DB (cleaner Czech error messages).
- `Skill.Name` unique within catalog — unique index.

## API

New endpoint group `SkillEndpoints.cs` mounted at `/api/skills`:

| Method | Route                 | Purpose                                                                 |
|--------|-----------------------|-------------------------------------------------------------------------|
| GET    | `/api/skills`         | list all skills with class restriction + building requirements          |
| GET    | `/api/skills/{id}`    | single skill                                                            |
| POST   | `/api/skills`         | create (name, class restriction, effect, notes, building req list)      |
| PUT    | `/api/skills/{id}`    | update all fields; building requirements replaced as a set              |
| DELETE | `/api/skills/{id}`    | delete; 409 if referenced by any recipe or by any GameSkill             |

Per-game skill endpoints added to `GameEndpoints.cs` (pattern copied from per-game item endpoints):

| Method | Route                                         | Purpose                                                        |
|--------|-----------------------------------------------|----------------------------------------------------------------|
| GET    | `/api/games/{gameId}/skills`                  | list skills active in game with XP cost + level requirement    |
| PUT    | `/api/games/{gameId}/skills/{skillId}`        | upsert: add skill to game with XP cost + optional level gate   |
| DELETE | `/api/games/{gameId}/skills/{skillId}`        | remove skill from game; 409 if any recipe in game requires it  |

Changes to `CraftingEndpoints.cs`:

- `CraftingRecipeDto` gets `RequiredSkillIds: int[]`.
- Upsert payload (`CreateRecipeRequest` / `UpdateRecipeRequest`) gets `RequiredSkillIds: int[]`.
- On save: each id in `RequiredSkillIds` must (a) exist in the Skills table, (b) have a `GameSkill` row for `recipe.GameId`. Otherwise 400 with Czech message indicating which skill is missing from the game.

New DTOs in `src/OvcinaHra.Shared/Dtos/`:

- `SkillDto`, `CreateSkillRequest`, `UpdateSkillRequest`
- `GameSkillDto`, `UpsertGameSkillRequest`
- Extended `CraftingRecipeDto`, `CreateCraftingRecipeRequest`, `UpdateCraftingRecipeRequest` with `RequiredSkillIds`

## UI

New pages:

- **`/skills`** (Dovednosti) — new directory `src/OvcinaHra.Client/Pages/Skills/`:
  - `Skills.razor`: `OvcinaGrid` + `DxPopup` detail, same pattern as Items/Monsters.
  - Columns: Název, Povolání, Efekt, Požadované budovy (comma-joined names), Poznámka.
  - Edit popup uses `DxFormLayout`:
    - Název — `DxTextBox`, required
    - Typ — toggle: class skill (pick `PlayerClass`) vs. adventurer ("Dobrodruh", `ClassRestriction = null`)
    - Efekt — `DxMemo`
    - Poznámka k požadavkům — `DxMemo`
    - Požadované budovy — add/remove list mirroring recipe pattern
    - Obrázek — upload
- **`/games/{id}/skills`** (Dovednosti ve hře) — new page under `Pages/Games/`:
  - Grid of `GameSkill` rows for the game: Název, Povolání, XP, Úroveň, remove button.
  - "+ Přidat dovednost ze seznamu" picker (dropdown of global Skills not yet in game).
  - Row click opens small popup to edit `XpCost` and `LevelRequirement`.

Navigation menu: add "Dovednosti" item in the catalog area alongside Items/Buildings.

**Crafting recipe dialog change** (the dialog in the attached screenshot, in `Pages/Items/`):

Between "Požadované budovy" and "Smazat recept", add a "Požadované dovednosti" block:

```
Požadované dovednosti: (Žádné.)
  • Tichý úder           [×]
  • Alchymie             [×]
  [ (přidat dovednost) ▼ ] [ + ]
```

The dropdown is populated from `GameSkill` for the current game — you cannot require a skill that isn't active in the game.

**Copy conventions:**

- Player/character references use "Hrdina" (sg.) / "Hrdinové" (pl.) per project convention.
- "Dobrodruh" is kept as the label for adventurer-skill category (it names the category, not the player).

**Shared helper:** `PlayerClass?.GetClassRestrictionLabel()` extension in `src/OvcinaHra.Shared/Extensions/` returns the class display name or "Dobrodruh" for null. Used in skill list cell template, popup, and any future error messages.

## Testing

Integration tests in `tests/OvcinaHra.Api.Tests/` (Testcontainers PostgreSQL, xUnit — project standard):

- `SkillEndpointsTests.cs`
  - create class skill + adventurer skill
  - update replaces building requirements as a set
  - list returns nav data eagerly-loaded
  - delete blocked when referenced by recipe or by any GameSkill
  - name required; name uniqueness enforced
- `GameSkillEndpointsTests.cs`
  - upsert adds skill to game, sets XP + optional level
  - second upsert updates values for same skill
  - `XpCost < 0` rejected; `LevelRequirement < 0` rejected
  - delete removes from game; 409 when a recipe in the game requires it
- Extend `CraftingEndpointsTests.cs`
  - recipe create with `RequiredSkillIds` persists links
  - recipe update replaces skill set (add + remove)
  - 400 when a skill isn't in the game
  - 400 when a skill id doesn't exist at all

E2E (Playwright) in `tests/OvcinaHra.E2E/`:

- `skills.spec.ts` — create skill in catalog, assign to game with XP + level, require in a recipe, save, reload, confirm persistence.

No mocks. Real PostgreSQL container, real HTTP.

## Migration & rollout

Single EF Core migration `AddSkillsDomain`:

- Create tables `Skills`, `SkillBuildingRequirements`, `GameSkills`, `CraftingSkillRequirements`.
- CHECK constraints on `GameSkills.XpCost`, `GameSkills.LevelRequirement`.
- Unique index on `Skills.Name`.
- FK cascade rules: deleting a Skill cascades to its building requirements, GameSkills, and CraftingSkillRequirements (matches DELETE semantics we enforce at endpoint level — but 409 blocks before it ever cascades in practice).

No data to backfill; all tables are new.

## Rulemaster bot integration (follow-up, outside this repo)

The Ovčina rulemaster bot already consumes the items API. After these endpoints ship:

1. OpenAPI / Scalar will auto-expose the new routes (built-in .NET 10 OpenAPI).
2. The rulemaster bot prompt/config must be updated to list the new endpoints so it knows Skills exist. Action: locate the rulemaster config (likely under `.claude/skills/rulemaster-ovcina/`) and add:
   - `GET/POST/PUT/DELETE /api/skills[/{id}]`
   - `GET/PUT/DELETE /api/games/{gameId}/skills[/{skillId}]`
   - Extended recipe payload field `RequiredSkillIds`.

This is tracked as the final task in the implementation plan but touches a different repo, so it's called out here.

## Open questions

None — all decisions locked during brainstorming (see question/answer trail in conversation).
