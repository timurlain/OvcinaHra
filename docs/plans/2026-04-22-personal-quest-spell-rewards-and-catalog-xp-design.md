# PersonalQuest — Spell rewards + catalog-level XP (design)

**Date:** 2026-04-22
**Status:** Approved — ready for implementation planning
**Branch:** `feat/personal-quest-spell-rewards-and-catalog-xp`

## Problem

Three related gaps in the current PersonalQuest domain:

1. **Spells are not a valid reward type.** Rewards today are Skills (many) and Items (many, with quantity). Game designers need to award spells — especially scrolls.
2. **XP is invisible from catalog view.** `XpCost` lives only on `GamePersonalQuest`. The catalog popup never shows it, so a user editing a quest template has no idea what XP cost the quest is supposed to have. The per-game card also only renders under three AND-ed conditions, so the XP input can be missed entirely.
3. **Quest-creation flow breaks** when missing dependencies (no item/skill/spell exists yet). This is Phase 2 (wizard); tracked separately and NOT part of this design.

## Scope of this design

Phase 1 — targeted patches to the existing `PersonalQuestList.razor` popup. No new page, no wizard. Addresses gaps #1 and #2 only.

## Decisions made with user

| Q | Answer |
|---|---|
| Wizard scope | Parallel flow (not a replacement) — Phase 2, separate |
| XP model | **Catalog default + per-game override**: `PersonalQuest.XpCost` (required), `GamePersonalQuest.XpCost` optional (nullable = use catalog) |
| Spell reward quantity | **Applies only when `IsScroll=true`**. Entity carries `Quantity`; UI hides spinner for non-scrolls and persists `1` |
| Validation | **UI + API**. Combobox filters to non-learnable client-side; API returns 400 if `spell.IsLearnable == true` |
| Version bump | 0.9.7 → **0.10.0** (minor, new reward type is a domain-level addition) |

## Domain model changes

### New entity: `PersonalQuestSpellReward`

```csharp
public class PersonalQuestSpellReward
{
    public int PersonalQuestId { get; set; }
    public PersonalQuest PersonalQuest { get; set; } = null!;

    public int SpellId { get; set; }
    public Spell Spell { get; set; } = null!;

    public int Quantity { get; set; } = 1;
}
```

**Configuration (`PersonalQuestSpellRewardConfiguration`):**
- Composite PK: `(PersonalQuestId, SpellId)`
- FK to `PersonalQuest`: cascade on delete (matches SkillReward / ItemReward pattern)
- FK to `Spell`: restrict on delete (a spell referenced as a reward cannot be deleted from the catalog without unlinking first — matches item FK behaviour)
- `Quantity`: default 1, min constraint left to API (no DB-level CHECK needed; dev-grade app)

### Modified entity: `PersonalQuest`

Add `public int XpCost { get; set; }` — default 0. Base XP cost for the quest.

Add navigation: `public ICollection<PersonalQuestSpellReward> SpellRewards { get; set; } = new List<PersonalQuestSpellReward>();`

### Modified entity: `GamePersonalQuest`

Change `XpCost` from `int` to `int?`. Semantic:
- `null` → use `PersonalQuest.XpCost` (catalog default)
- non-null → per-game override

### DbContext

Add `DbSet<PersonalQuestSpellReward> PersonalQuestSpellRewards`.

## DTO changes

### New

```csharp
public record AddSpellRewardDto(int SpellId, int Quantity = 1);

public record PersonalQuestSpellRewardSummary(int SpellId, string SpellName, bool IsScroll, int Quantity);
```

### Modified

`PersonalQuestListDto`:
- Add `int XpCost` (the catalog value)
- Add `IReadOnlyList<PersonalQuestSpellRewardSummary> SpellRewards`
- Update computed `RewardSummary` (server-side) to include spell rewards: e.g. `"+ Svitek Ohnivého šípu ×3"`

`PersonalQuestDetailDto`: mirror the above additions.

`CreatePersonalQuestDto`, `UpdatePersonalQuestDto`: add `int XpCost`.

`GamePersonalQuestListDto`:
- `XpCost` becomes `int?`
- Add computed `int EffectiveXpCost => XpCost ?? PersonalQuest.XpCost` (server-side)

`CreateGamePersonalQuestDto`, `UpdateGamePersonalQuestDto`: `XpCost` → `int?`.

## API surface

Added under `/api/personal-quests`:

| Verb | Path | Behaviour |
|---|---|---|
| POST | `/{id}/spell-rewards` | Body: `AddSpellRewardDto`. **Returns 400** if `spell.IsLearnable == true`. Returns 404 if spell or quest missing. Returns 201 on success. |
| DELETE | `/{id}/spell-rewards/{spellId}` | Returns 204. 404 if the reward link doesn't exist. |

Existing endpoints (Create/Update PersonalQuest, game-link CRUD) gain `XpCost` in their DTO payloads — no new endpoint needed.

## UI changes (`PersonalQuestList.razor`)

### 1. XP in the main form

Add an always-visible `DxSpinEdit` for `XpCost` in the quest form (bound to `model.XpCost`). Label: **"XP cena"**. Placed next to Difficulty for visibility.

### 2. Per-game card XP override

The existing spinner in the per-game card becomes nullable:
- `@bind-Value="@gpqXpCost"` (nullable int)
- `NullText="@($"výchozí: {model.XpCost}")"` — shows the catalog default when no override
- Visual hint below: when `gpqXpCost is null`, render small text `"(bez přepisu — použije se hodnota {model.XpCost})"`

### 3. New Spell rewards section

Mirror the Items section exactly. Located under the existing Skills and Items reward blocks. Renders only when `editingId.HasValue`.

```
<h6>Kouzla</h6>
{badges for existing PersonalQuestSpellReward rows}
<DxComboBox
  Data="@SpellsNotYetAdded"                    (*)
  @bind-Value="@newSpellId"
  TextFieldName="Name" ValueFieldName="Id"
  NullText="(přidat kouzlo)"
  SearchMode="ListSearchMode.AutoSearch" />

@if (newSpellIsScroll) {
  <DxSpinEdit @bind-Value="@newSpellQuantity" MinValue="1" />
}
<button>Přidat</button>
```

`SpellsNotYetAdded` filters:
- catalog spells with `IsLearnable == false`
- excludes spells already in `detailSpellRewards`

`newSpellIsScroll` is derived from the currently selected spell. When false, `newSpellQuantity` is forced to 1 and the spinner is hidden.

Badge display: `{SpellName} ×{Quantity}` if `Quantity > 1`, else just `{SpellName}`.

### 4. Game grid

Add `EffectiveXpCost` column (caption "XP cena"). Keep existing `XpCost` column available but hidden-by-default as "XP (jen override)" for users who want to see only overrides.

**`LayoutKey` bump required** — column semantics changed.

## Migration strategy

Name: `AddPersonalQuestSpellRewardsAndCatalogXp`

Operations:
1. `AddColumn XpCost (int, default 0, NOT NULL)` on `PersonalQuests`
2. `AlterColumn XpCost (int NULL)` on `GamePersonalQuests` — existing values preserved as explicit overrides
3. `CreateTable PersonalQuestSpellRewards` with composite PK, FKs, Quantity column

All additive or nullability-relaxing — safe under a running app. Runs automatically via `db.Database.MigrateAsync()` at startup (established pattern).

**Data caveat:** Existing catalog quests get `XpCost = 0` on migration. Existing per-game links preserve their current value as an explicit override. After deploy, user walks through catalog quests once to set proper catalog defaults — lightweight (a handful of rows).

## Testing (integration, Testcontainers PostgreSQL)

New API tests in `PersonalQuestEndpointTests`:
- `SpellReward_LearnableSpell_Returns400`
- `SpellReward_NonLearnable_Returns201`
- `SpellReward_Scroll_QuantityGreaterThanOne_Persisted`
- `SpellReward_NonScroll_QuantityForcedToOne` — API-level: if UI sends Quantity>1 for non-scroll, we accept but this behaviour is documented (UI prevents it)
- `Delete_SpellReward_Returns204`
- `CreateQuest_WithXpCost_Persisted`
- `GamePersonalQuest_XpCostNull_EffectiveFallsBackToCatalog`
- `GamePersonalQuest_XpCostSet_EffectiveEqualsOverride`

Existing tests that create `GamePersonalQuest` with explicit `XpCost`: update DTOs to `int?`, most assertions unchanged.

## Version & deploy

- `OvcinaHra.Client.csproj`: 0.9.7 → **0.10.0**
- Auto-deploys on merge to `main` — migration runs at Api startup
- No manual data backfill required; user handles catalog XP defaults via UI after deploy

## Out of scope (explicit)

- **Quest Maker Wizard** — Phase 2, separate design
- Spell reward image/icon rendering in reward badges — use plain text for now
- Per-kingdom spell-reward limits — use the existing `PerKingdomLimit` on `GamePersonalQuest` if that becomes relevant
- Default XP per Difficulty (auto-suggest) — can be layered later
