# PersonalQuest Domain — Design

> Date: 2026-04-19
> Status: Ready for implementation planning
> Related: follows the Item/Skill catalog pattern established by PRs #30 (Skills) and #32 (Items grid split)

## Overview

A **PersonalQuest** is a per-character mini-quest (e.g. "Druid", "Artefaktor", "Tajnou cestou") sold to players at merchants, costing XP to start and rewarding Skills, Items, or flavour text on completion. Organizers need to:

- Maintain a **world-level catalog** of quest templates (Name, Description, class restrictions, difficulty, card text, linked rewards).
- Opt templates into a given **game** with per-game config (XP cost, per-kingdom availability).
- **Assign** a quest to a character in a game (max one per character; `CompletedAtUtc` tracked for future reporting).

The feature follows the same catalog/game split pattern the app already uses for Items and Skills, so organizers browsing `/personal-quests` see the same UX they already know.

## Entities

### `PersonalQuest` (catalog)

| Field | Type | Purpose |
|---|---|---|
| `Id` | `int`, PK | identity |
| `Name` | `string`, required, unique, max 200 | Název |
| `Description` | `string?`, max 500 | Popis — short public tagline |
| `Difficulty` | `TreasureQuestDifficulty` enum (stored as string) | Obtížnost — reused existing enum (Lehká / Střední / Těžká) |
| `AllowWarrior` | `bool`, default `false` | class matrix bit 1 |
| `AllowArcher` | `bool`, default `false` | bit 2 |
| `AllowMage` | `bool`, default `false` | bit 3 |
| `AllowThief` | `bool`, default `false` | bit 4 — all four `false` = no restriction |
| `QuestCardText` | `string?` | Kartička questu — multi-step task text (plain markdown, printer parses later) |
| `RewardCardText` | `string?` | Kartičky odměn — how the rewards work mechanically |
| `RewardNote` | `string?` | Ostatní odměny — freeform for rewards not linked to Skills/Items |
| `Notes` | `string?` | Poznámky — organizer-only |
| `ImagePath` | `string?` | blob key, resolved to SAS URL on read |

Navigation:

- `SkillRewards` → `PersonalQuestSkillReward[]`
- `ItemRewards` → `PersonalQuestItemReward[]`
- `GameLinks` → `GamePersonalQuest[]`
- `CharacterAssignments` → `CharacterPersonalQuest[]`

Unique index on `Name`.

### `PersonalQuestSkillReward` (link table)

```
PersonalQuestId   PK, FK → PersonalQuest, cascade
SkillId           PK, FK → Skill,          restrict
```

Deleting a `PersonalQuest` removes its skill-reward links. Deleting a `Skill` is blocked if any quest still references it (restrict protects reward definitions).

### `PersonalQuestItemReward` (link table)

```
PersonalQuestId   PK, FK → PersonalQuest, cascade
ItemId            PK, FK → Item,          restrict
Quantity          int, default 1, CHECK >= 1
```

Same cascade/restrict semantics.

### `GamePersonalQuest` (per-game config)

```
GameId              PK, FK → Game,          cascade
PersonalQuestId     PK, FK → PersonalQuest, cascade
XpCost              int, CHECK >= 0
PerKingdomLimit     int?, CHECK IS NULL OR >= 1   -- null = unlimited
```

Mirrors `GameItem` / `GameSkill`. Organizer creates a row by opting a quest into a game. Total game-wide availability = `PerKingdomLimit × number_of_kingdoms`, derived — not stored.

### `CharacterPersonalQuest` (assignment)

```
CharacterId        PK, FK → Character, cascade    -- one-to-one: a character has at most 1 personal quest
PersonalQuestId    FK → PersonalQuest, restrict
AssignedAtUtc      DateTime, default now()
CompletedAtUtc     DateTime?                      -- null until organizer marks complete
```

PK = `CharacterId` natively enforces the "one personal quest per character per game" rule (since Character already has a `GameId`, this is implicitly per-game).

## API endpoints

Grouped under `/api/personal-quests` (minimal API `MapGroup`, following `ItemEndpoints.cs` layout):

| Verb | Route | DTO in → out |
|---|---|---|
| GET | `/` | — → `List<PersonalQuestListDto>` |
| GET | `/{id}` | — → `PersonalQuestDetailDto` |
| POST | `/` | `CreatePersonalQuestDto` → `PersonalQuestDetailDto` |
| PUT | `/{id}` | `UpdatePersonalQuestDto` → `NoContent` |
| DELETE | `/{id}` | — → `NoContent` |
| GET | `/by-game/{gameId}` | — → `List<GamePersonalQuestListDto>` (merged: catalog + GPQ + RewardSummary) |
| POST | `/game-link` | `CreateGamePersonalQuestDto` → `GamePersonalQuestDto` |
| PUT | `/game-link/{gameId}/{pqId}` | `UpdateGamePersonalQuestDto` → `NoContent` |
| DELETE | `/game-link/{gameId}/{pqId}` | — → `NoContent` |
| POST | `/{id}/skill-rewards` | `{ SkillId }` → 201 |
| DELETE | `/{id}/skill-rewards/{skillId}` | — → `NoContent` |
| POST | `/{id}/item-rewards` | `{ ItemId, Quantity }` → 201 |
| DELETE | `/{id}/item-rewards/{itemId}` | — → `NoContent` |

Character-assignment endpoints ship in a follow-up PR together with the UI.

### DTO shape notes

- `PersonalQuestListDto` — positional params for all catalog columns, plus `[JsonIgnore]` computed `ClassRestrictionDisplay` (`"Válečník, Mág"` or `"Všechna povolání"`) and `DifficultyDisplay` (via `GetDisplayName()`), plus `HasImage` computed.
- `GamePersonalQuestListDto` — catalog fields + `XpCost`, `PerKingdomLimit`, `RewardSummary` (string, `"Dovednost druid │ Kouzlo Léčivá dlaň ×1"`, server-built by joining skill + item rewards).
- Reward summary format matches the item-recipe summary shipped in PR #32.

## UI

### Route

`/personal-quests` — accepts `?catalog=true`. Default view (no query) is "Jen tato hra".

### Nav

Added to both sections at matching positions (right after `Questy`):

- **Hra** section: `NavLink href="personal-quests"` → "Osobní questy", icon `bi bi-person-workspace`
- **Katalog světa** section: `NavLink href="personal-quests?catalog=true"` → "Osobní questy", same icon

### Two grid views (catalog / game), same split pattern as `/items`

**Common columns — identical order in both views:**

| # | Field | Default visible? |
|---|---|---|
| 1 | `Id` | hidden |
| 2 | `Name` | **visible** |
| 3 | `ClassRestrictionDisplay` | **visible** |
| 4 | `AllowWarrior` | hidden |
| 5 | `AllowArcher` | hidden |
| 6 | `AllowMage` | hidden |
| 7 | `AllowThief` | hidden |
| 8 | `DifficultyDisplay` | **visible** |
| 9 | `Description` | **visible** |
| 10 | `RewardSummary` | **visible** |
| 11 | `RewardNote` | hidden |
| 12 | `QuestCardText` | hidden |
| 13 | `RewardCardText` | hidden |
| 14 | `Notes` | hidden |
| 15 | `HasImage` | hidden |

**Catalog-only extras (appended):** `Přidat/Odebrat` action column (same UX as `/items?catalog=true`).

**Game-only extras (appended):**

| # | Field | Default visible? |
|---|---|---|
| 16 | `XpCost` | **visible** |
| 17 | `PerKingdomLimit` | **visible** |

**Game-only prefix (leftmost, fixed):** three-dots context menu with items:

- **Upravit** (bi-pencil-fill) — opens detail popup
- **Odebrat z hry** (bi-x-circle) — removes the GamePersonalQuest row (keeps catalog entry)
- **Smazat** (bi-trash-fill, text-danger) — deletes the catalog entry (confirm popup)

LayoutKeys: `grid-layout-personal-quests-catalog` and `grid-layout-personal-quests-game`.

### Detail popup (DxPopup, width ~900px)

`DxFormLayout`:

- Název (text)
- Popis (text)
- Obtížnost (combobox)
- Povolání — group of 4 checkboxes (Válečník, Střelec, Mág, Zloděj)
- Kartička questu (DxMemo)
- Kartičky odměn (DxMemo)
- Ostatní odměny (textbox)
- Poznámky (DxMemo)
- ImagePicker (if editing)

**Rewards card** (only when editing):
- Dovednosti — chip list with `DxComboBox` picker + add button
- Předměty — chip list with `DxComboBox` picker + qty `DxSpinEdit` + add button

**Per-game card** (only when a game is selected and not in catalog mode, same pattern as Item popup):
- XP za získání (`DxSpinEdit`, MinValue=0)
- Limit na království (`DxSpinEdit`, MinValue=1, NullText="(neomezeno)")
- "Přidat do hry" / "Odebrat z hry" / "Uložit nastavení" buttons

## Deferred (explicitly NOT in this PR)

1. **Character assignment UI** — data table ships; form/page to pick a quest for a character comes next.
2. **Completion tracking UI** — `CompletedAtUtc` exists; no toggle or report yet.
3. **Card printing** — `QuestCardText` stays plain markdown; printer is a separate project.
4. **Merchant/Kingdom modelling** — `PerKingdomLimit` is a number, no `Kingdom` or `Merchant` entity; enforcement is organizational.
5. **Per-kingdom-per-quest stocking lists** — implied by the `PerKingdomLimit` int but not persisted per kingdom.

## Migration

EF Core migration `AddPersonalQuestDomain` creates four new tables (`PersonalQuests`, `PersonalQuestSkillRewards`, `PersonalQuestItemRewards`, `GamePersonalQuests`, `CharacterPersonalQuests`). All additive, no modifications to existing tables. Safe to deploy to production with `db.Database.MigrateAsync()` on API startup.

## Versioning

Client version bump **0.5.3 → 0.6.0** (new domain entity = minor version bump, per project convention).
