# Design Brief — Monster detail `/monsters/{id}`

**Status:** v1 · 2026-04-23
**Target surface:** Claude Design (Ovčina org, design system inherited)
**Implementation target:** new `src/OvcinaHra.Client/Pages/Monsters/MonsterDetail.razor`

## Why it matters

The bestiary is an iconic Ovčina surface — organizers want to **feel** the creature, not stare at a row of integer stats. Current MonsterList popup is a functional form, not a creature card.

## Current state

- `MonsterList.razor` at `/monsters` — DxGrid with Name · TypDisplay · Category · Attack · Defense · Health · RewardXp · RewardMoney · TagsDisplay · (hidden) Abilities / AiBehavior / RewardNotes / Notes
- Row click → popup edit form (single tab)
- Entity: `Monster { Name, Category (int level/rank), MonsterType (enum), Abilities, AiBehavior, Stats (CombatStats VO: Attack, Defense, Health), RewardXp, RewardMoney, RewardNotes, Notes, ImagePath }`
- Relationships: `MonsterTagLink[]`, `MonsterLoot[]` (items dropped per game), `GameMonster[]` (per-game assignments), `QuestEncounter[]`

## Data prereqs — none

Everything is on the entity + existing DTOs (`MonsterListDto`, `MonsterDetailDto`, `MonsterLootDto`).

## Prompt for Claude Design

```
Design the Monster detail page at /monsters/{id:int} for OvčinaHra —
the organizer's bestiary entry for a single creature. Feel: illustrated
bestiary page, not an RPG stat block.

AUDIENCE & DEVICE
Organizers planning encounters, desktop first (1440 nominal), 10"
tablet secondary. Czech only.

AESTHETIC
This page is more "illuminated bestiary" than the location card.
Parchment stays, but the creature illustration is the dominant visual
(4:3 landscape). Stats read like a block quote in the margin, not a
spreadsheet. AiBehavior and Abilities get room to breathe.

PAGE SKELETON

  Breadcrumb:  Příšery / {Name}
  Title strip (sticky):
    [← zpět]  {Name}  (h1, Merriweather 900, 2rem)
    Subline:  MonsterType chip · Kategorie N · úroveň-badge mono
    Right actions: [Upravit] / [Smazat] (read mode)
                   [Zrušit] / [Uložit]    (edit mode)

  Tab strip (sticky, default=Karta):
    Karta · Upravit · Bojovka · Kořist · Výskyt

============================================================
TAB 1 — Karta (bestiary page)
============================================================

Two-column layout on desktop (65fr 35fr):

  LEFT: creature illustration at 4:3 (aspect-ratio:4/3, object-fit:
  cover), heavy parchment frame (2 px #c4b494 border, inset gold
  radial, green-tinted shadow). Caption below: MonsterType · Kategorie.

  RIGHT: stats block — vertical list, each row is:
    [icon] LABEL ........... value
  Using:
    bi-lightning-charge  Útok      12
    bi-shield-shaded     Obrana     8
    bi-heart-pulse       Životy    45
    bi-star-fill         XP       150
    bi-coin              Groše     20
  Mono values, right-aligned. Subtle hairline between rows.

BELOW both columns:
  h3 "Schopnosti" + paragraph (Abilities field, Georgia serif,
  left-aligned, hyphens auto). If empty, show italic grey
  "Žádné zvláštní schopnosti zaznamenány."

  h3 "AI chování" + same treatment (AiBehavior field).

  h3 "Tagy" + horizontal list of tag chips (burnt-orange outline,
  muted parchment bg). Existing tag palette — no kingdom colors.

  Organizer notes footer: small italic "Poznámky" block if Notes
  is set; same muted beige callout treatment.

============================================================
TAB 2 — Upravit
============================================================

DxFormLayout 2-col sections:

  Identita:    Název · MonsterType (DxComboBox, Czech chips)
               Kategorie (DxSpinEdit 0-10)
  Vizuál:      ImagePicker locations-style, EntityType="monsters",
               AspectRatio="4:3", Width="360px"
  Bojovka:     Attack · Defense · Health  (3-col SpinEdit row)
               Abilities (DxMemo, Rows=4)
               AiBehavior (DxMemo, Rows=3)
  Odměna:      RewardXp · RewardMoney  (2-col SpinEdit row)
               RewardNotes (DxMemo, Rows=2)
  Tagy:        Multi-select chip picker from existing Tag catalog
  Poznámky:    Notes (DxMemo, Rows=3)

Footer fixed at page bottom during scroll:
  Smazat (ghost-bordeaux, far-left, only when editing) · spacer
  Zrušit (secondary) · Uložit (primary, disabled until Name filled)

============================================================
TAB 3 — Bojovka (combat sheet — presenter view for event runners)
============================================================

Large-type presenter view, optimized for the organizer glancing
at a tablet during a live encounter:
  · Creature illustration (smaller, 3:2, top)
  · Three big stat boxes across: ÚTOK / OBRANA / ŽIVOTY in 3rem
    mono numerals, parchment-alt background, burnt-orange ribbon
  · Abilities rendered as numbered bullets (1. 2. 3. …) in 1.1rem
  · AI chování rendered as a pull-quote (Georgia italic, indented)
  · Sticky top: quick [−1 Život] [+1 Život] counter — NO state
    (advisory only, local to session)

This tab is read-only; edits happen on Upravit.

============================================================
TAB 4 — Kořist (loot table, per-game)
============================================================

Per-game grouping. For each Game where the monster is active:
  · Game header (Name, Edition badge)
  · Row of item mini-tiles — 5:7 MTG-sized, same pattern as
    LocationStashTile but each tile is an Item drop:
      · Item image
      · Item name (Merriweather)
      · "× N" multiplier chip (if Quantity > 1)
  · "+ Přidat kořist" small button at end of row

When no loot assigned in a game: "Bez kořisti" muted italic.

Admin-only header row: "+ Přidat do hry" — wraps MonsterListDto's
per-game assignment endpoint.

============================================================
TAB 5 — Výskyt (where this monster appears)
============================================================

Compact table:
  | Hra | Questy (N) | Encounters (N) | Odebrat |

Row click → navigates to that game's monster page or quest page
(designer's judgement). Odebrat confirms via DxPopup (MEMORY §D).

============================================================
STATES TO RENDER
============================================================

  1. Karta populated — example: "Skřetí lukostřelec" (Goblin archer),
     Category 2, Humanoid, full stats, 2 abilities, 3 tags, small
     notes footer.
  2. Upravit — form populated with same creature.
  3. Bojovka — presenter view with the big stat boxes.
  4. Kořist — 2 games, 3 items in current game (one with ×2 pill),
     empty state in prior game.
  5. Karta empty — new creature, no image, no abilities; Drozd perch
     empty-state in the illustration slot.

ANTI-PATTERNS
  · No kingdom colors on MonsterType chips.
  · No emoji.
  · No cinematic 16:9 illustration — locked 4:3 matches the existing
    game-art format.
  · No Drozd on populated tabs.
  · The "counter" on Bojovka is advisory only — do NOT persist.
```

---

## Builder addendum — Monster detail

TARGET FILES
- New page: `src/OvcinaHra.Client/Pages/Monsters/MonsterDetail.razor` (route `/monsters/{id:int}`)
- Theme additions: `.oh-mon-*` scoped block in `ovcinahra-theme.css`
- New component: `src/OvcinaHra.Client/Components/MonsterLootTile.razor` (reuses 5:7 MTG pattern)
- Optional: `src/OvcinaHra.Client/Components/TagChipPicker.razor` — multi-select chip from Tag catalog (reusable for Quests, Items)

DATA BINDINGS
- GET `/api/monsters/{id}` → `MonsterDetailDto`
- Upravit form → PUT `/api/monsters/{id}` with `UpdateMonsterDto`
- Kořist tab → GET `/api/monsters/{id}/loot?gameId=X` → `List<MonsterLootDto>`
  (verify endpoint; may need to add per-game-grouped variant)
- Výskyt → `GameMonster[]` from dedicated endpoint

NOTES
- `Category` is an integer (looks like level/rank). Cap at 0-10 in the SpinEdit; user can adjust.
- `CombatStats` is a value object (Attack/Defense/Health). Edit flat in the form; rebuild the VO on save.
- Existing `TagsDisplay` on `MonsterListDto` is a computed `string.Join(", ", TagNames)`. On the detail DTO we have `List<TagDto> Tags` — use those.
- The "counter" on Bojovka tab should be purely local (a simple `int tempHp` state) — no API call, no persistence.
- Big stat boxes on Bojovka may benefit from `font-variant-numeric: tabular-nums` so digits align.
