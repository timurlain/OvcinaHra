# Design Brief — Monster grid `/monsters`

**Status:** v1 · 2026-04-23
**Target surface:** Claude Design (Ovčina org, design system inherited)
**Implementation target:** rewrite `src/OvcinaHra.Client/Pages/Monsters/MonsterList.razor`

## Why it matters

The bestiary list should **read like an index of creatures**, not a spreadsheet of integers. Current grid is 13 columns wide and feels like a report — organizers scan for "which creature fits this encounter," not sort-by-attack.

## Current state

- Dual mode (per-hra / catalog)
- DxGrid with 13 columns, 9 hidden by default
- Row click → single-tab popup edit form
- `LayoutKey = "grid-layout-monsters-v2"` (bump to v3 on ship)
- Search, filters minimal

## User decisions already locked

| Decision | Value |
|---|---|
| Dual mode | Keep both |
| Layout | **Hybrid** — card-row list, not a pure grid. Each row is a full-width horizontal card with a thumbnail + stats inline |
| Thumbnail | Left edge of row, 96 × 72 px (4:3), same as detail page illustration ratio, rounded corners |
| Stats visible inline | ÚT · OBR · ŽIV · XP · Groše (5 compact mono chips on each row) |
| Tags | Small chip cluster inline |
| Filter strip | Hledat · Druh (MonsterType) · Kategorie range · Tagy multi-select · toggle "jen s kořistí" · toggle "jen nepřiřazené" (catalog-only) |
| Row click | Opens a quick-peek popup (the Karta tab view) or navigates to `/monsters/{id}` — **designer's call** |
| Row actions | Hover icons on right edge: Upravit · Přidat kořist · Zkoušet (navigates to Bojovka tab) · Smazat |

## Prompt for Claude Design

```
Design the Monster bestiary list at /monsters for OvčinaHra — organizer
view of all creatures in the LARP world. Card-row hybrid between a
grid and an image list.

AUDIENCE & DEVICE
Organizers browsing for encounter planning. Desktop-first 1440 nominal,
tablet 10". Czech only.

MODES (?catalog=true)

  Per-hra (default): creatures assigned to the current game.
  Katalog: all creatures, plus per-row "Přiřadit ke hře" pill.

LAYOUT

Title strip:
  h1 "Příšery"
  Mode toggle: [Jen tato hra] [Katalog]  (segmented control)
  Right:       [+ Nová příšera] primary

Filter row (sticky below title):
  [ Hledat… ]  [ Druh ▼ ]  [ Kategorie ▼ ]  [ Tagy ▼ ]
  [ toggle s kořistí ]  [ toggle nepřiřazené (katalog) ]
  Right-aligned: [ N příšer ] count

CARD-ROW LIST (not DxGrid, scrollable div)

Each monster row is a full-width horizontal card, 96 px tall,
parchment-cream bg, 1 px warm-beige border, 6 px radius, green-tinted
shadow. Content left→right:

  [ thumbnail 96×72 4:3, rounded ]

  [ Name (Merriweather 700 1rem, forest green)
    MonsterType chip · Kategorie N mono badge · 3 tag chips ]

  [ Spacer grows ]

  [ stat pill cluster (5 pills, monospace):
      ÚT 12   OBR 8   ŽIV 45   XP 150   GR 20
    Each pill: 8 px padding, surface-alt bg, burnt-orange outline for
    the label, mono value. On mobile/tablet, stack or wrap. ]

  [ Hover icons appearing on right edge on row hover (0 → 0.8 opacity):
      bi-pencil      Upravit
      bi-plus-square Přidat kořist
      bi-play-fill   Zkoušet (→ /monsters/{id} tab=Bojovka)
      bi-trash       Smazat (ghost-bordeaux)
    28×28 each, 6 px gap ]

Row hover: background shifts to sage mist, image slightly brightens,
icons fade in. Row click (anywhere outside the icons) opens a
720 × 520 quick-peek popup.

QUICK-PEEK POPUP
Shows the Karta view (creature illustration 4:3 at 400 px + right-
column stats block + ability paragraph + AI-behavior paragraph), plus
footer [Zavřít] [Otevřít detail →] (navigates to /monsters/{id}).

EMPTY STATE (per-hra)
Drozd 120×120 + "Žádné příšery přiřazeny k této hře." +
[Otevřít katalog] secondary + [+ Nová příšera] primary.

STATES TO RENDER (stack vertically)

  1. Per-hra mode with 6 card rows: mix of high/low stats (Skřetí
     lukostřelec ÚT 12, Vlkodlak ÚT 25, Krysa ÚT 2). One row hovered
     showing icons + sage tint.
  2. Catalog mode with 6 rows; 2 already-assigned (muted "✓" pill)
     and 4 not-yet-assigned (hover-reveal "+ Přiřadit").
  3. Empty state with Drozd.
  4. Quick-peek popup — example "Vlkodlak", full Karta view.

ANTI-PATTERNS
  · No DxGrid with 13 columns.
  · No kingdom colors on MonsterType chips.
  · No emoji — Bootstrap Icons only.
  · No reliance on tiny icon-only badges without text labels —
    organizers need to scan by name, not decipher pictograms.
```

---

## Builder addendum — Monster grid

TARGET FILES
- Rewrite `src/OvcinaHra.Client/Pages/Monsters/MonsterList.razor` — drop DxGrid for the main list (keep DxPopup for quick-peek)
- Theme additions: `.oh-mon-row-*` scoped block in `ovcinahra-theme.css`
- Optional: `src/OvcinaHra.Client/Components/MonsterRow.razor` for the card row

DATA BINDINGS
- `GET /api/monsters` / `GET /api/monsters/by-game/{gid}` already returns `MonsterListDto` with all the fields + `TagNames`. No API changes.
- Quick-peek popup loads `MonsterDetailDto` via `GET /api/monsters/{id}` (for ImagePath + full Abilities + AiBehavior)

GRID COLUMN CHANGES
- `LayoutKey` concept doesn't apply — this is no longer a DxGrid (MEMORY §L still applies: if you KEEP a DxGrid anywhere, bump its LayoutKey)
- Drop DxGrid persistence; the card-row list doesn't need user-configurable columns

NOTES
- Filters could still persist in localStorage manually (e.g. Hledat text, selected filter chips). Optional.
- MonsterType chip colors: keep the neutral palette (no semantic color per type unless canon exists — check `hra-ovcina-tinkerer` skill memory for any MonsterType color mapping).
- Tag chips reuse the same style as LocationDetail's `.oh-lc-tag` — worth extracting to `.oh-tag` as a shared rule.
- Quick-peek popup CAN reuse the MonsterDetail Karta tab component if we refactor the Karta section into its own component. Call it out in the PR if you do.
