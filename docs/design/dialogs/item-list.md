# Design Brief — Item grid `/items`

**Status:** v1 · 2026-04-23
**Target surface:** Claude Design (Ovčina org, design system inherited)
**Implementation target:** rewrite `src/OvcinaHra.Client/Pages/Items/ItemList.razor`

## Why it matters

Items are **real hand-photographed LARP artifacts** — weapons, potions, jewelry, hand-sewn card props. The photo IS the artifact's identity. The current grid hides the photo behind a `HasImage` boolean column — organizers browsing by name can't see what they're looking at.

At the same time, in per-game mode the operational view matters — price, stock, unique/limited flags — because organizers run the shop economy from this page.

## Current state

- Dual mode: `/items` (per-hra) vs `?catalog=true` (all items across games)
- DxGrid with Name · ItemTypeDisplay · Effect · class requirement columns · unique/limited flags · (hidden) HasImage
- Per-hra mode adds Price / StockCount / IsSold / SaleCondition / IsFindable / RecipeSummary columns
- Row click → popup edit form
- `LayoutKey = "grid-layout-items-catalog-v2"` — bump to v3 on ship

## Entity facts (for the prompt)

- 19 ItemTypes in Czech: Zbraň · Štít · Zbroj · Helma · Střelná zbraň · Kůň · Ovládaný tvor · Životy · Svitek · Lektvar · Surovina · Peníze · Herní zvíře · Drobný artefakt · Šperk · Artefakt · Ingredience · Komodita · Ostatní.
- `ClassRequirements` value object: 4 integers (Warrior/Archer/Mage/Thief) — a class can use the item if the player's class level ≥ the Req for that class.
- `IsUnique` (one-of-a-kind) and `IsLimited` (bounded supply) are separate flags.
- `IsCraftable` flag; real crafting tree lives in `CraftingRecipe` per-game.

## User decisions already locked

| Decision | Value |
|---|---|
| Dual mode | Keep both |
| Primary visual | **Photo gallery in catalog mode** · **card-row in per-hra mode** — hybrid, because catalog is visual browse and per-hra is operational shop management |
| Photo aspect | **4:3 landscape** (smartphone photo format) — real LARP props are shot this way. Let designer confirm 4:3 vs 3:2 in the mockup. |
| Class-requirement display | Four tiny pips — Warrior (sword icon) / Archer (bow) / Mage (staff) / Thief (dagger). Muted when Req=0, lit in forest-green when Req≥1. Hover tooltip with exact req level. |
| Unique/Limited flags | Small ribbon/badge on the tile — gold ribbon top-right for "unikát", amber stripe for "limit". |
| Grid thumbnail (catalog) | Gallery tile: 4:3 photo at top, name, type chip, class pips, unique/limited ribbon |
| Per-hra row | Card-row: left-side 4:3 photo thumb (96×72) + identity + **price/stock pills prominently** (mono) + IsSold/IsFindable toggles on hover |

## Prompt for Claude Design

```
Design the Items page at /items for OvčinaHra — organizer view of every
item in the LARP world. Real hand-photographed weapons, armor, potions,
jewelry. The photo IS the artifact; the grid should treat it as such.

AUDIENCE & DEVICE
Organizers. Two different jobs on the same page:
  · Catalog mode — visual browse for encounter planning, quest reward
    picking, monster loot assignment
  · Per-hra mode — operational view of the game's shop: prices, stock,
    sale flags

Desktop-first, 10" tablet secondary. Czech only.

MODES (?catalog=true)

  Per-hra (default)
  Katalog

LAYOUT DIFFERS BY MODE — this is the key decision for the page.

============================================================
CATALOG MODE — Photo gallery
============================================================

Title strip:
  h1 "Katalog předmětů"
  Mode toggle: [Jen tato hra] [Katalog]
  Right:  [+ Nový předmět] primary

Filter row (sticky below title):
  [ Hledat… ]  [ Typ ▼ (19 types, searchable) ]  [ Třída ▼ ]
  [ toggle jen unikáty ]  [ toggle jen limit ]  [ toggle je craftable ]
  Right: [ N předmětů ] count

Gallery: responsive CSS grid of 4:3 photo tiles.
  · Desktop (≥1280 px): 5 cols
  · Desktop (1024–1280): 4 cols
  · Tablet (768–1024): 3 cols
  · Mobile: 2 cols
  · Gap: 24 px row, 20 px col

TILE COMPOSITION (4:3 landscape, 220 × 165 px desktop)
  · Photo top, object-fit:cover, fallback muted parchment gradient
  · Ribbon top-right if IsUnique: gold satin band with "UNIKÁT" (small mono)
  · Stripe top-left if IsLimited: amber stripe with "LIMIT"
  · Body (parchment cream, 12 px padding):
      - Name (Merriweather 700 0.9375rem, forest green, 2-line clamp)
      - ItemType chip (small, neutral)
      - Class pip row: 4 small icons (Warrior sword · Archer bow · Mage
        staff · Thief dagger). Muted when Req=0, forest-green filled
        when Req≥1, tooltip shows exact req value.
  · Hover: lift -2 px, sage tint, shadow deepens

============================================================
PER-HRA MODE — Card-row shop view
============================================================

Title strip:
  h1 "Předměty"
  Mode toggle: [Jen tato hra] [Katalog]
  Right:  [+ Nový předmět] primary

Filter row: same as catalog, plus:
  [ toggle IsSold ]  [ toggle IsFindable ]

CARD-ROW LIST (not DxGrid)

Each row: 96 px tall, full-width, parchment bg, warm-beige border,
green-tinted shadow. Content left→right:

  [ photo thumb 96×72 4:3 rounded corners ]
  [ Name (Merriweather 700) · ItemType chip · class pips ]
  [ spacer ]
  [ price-stock cluster (mono pills):
      [ Cena 15 g ]   [ Skladem 3 / 10 ]   [ Prodává ANO ]
      [ Nalezitelný ANO ]   [ Podmínka: "jen hrdinové 5+" ]
    Price pill uses the gold accent (Groše color). If IsSold=false,
    price pill is muted/struck. ]
  [ hover icons: bi-pencil Upravit · bi-boxes Crafting · bi-trash Smazat ]

Row click (outside icons) opens a quick-peek popup — 720×480 with the
Karta view (hero photo + core fields + effect text). Popup footer:
[Zavřít] [Otevřít detail →] navigates to /items/{id}.

EMPTY STATE (per-hra)
Drozd 120×120 + "Žádné předměty přiřazeny k této hře." +
[Otevřít katalog] secondary + [+ Nový předmět] primary.

CLASS PIP ICONS (proposal — confirm in mockup)
  Warrior → bi-shield-shaded         (forest green when lit)
  Archer  → bi-bullseye               (forest green when lit)
  Mage    → bi-magic                  (forest green when lit)
  Thief   → bi-incognito              (forest green when lit)
Each pip is 18×18, muted at #d4c4b0 when Req=0. Stack horizontally with
6 px gaps.

STATES TO RENDER (stack vertically)

  1. Catalog gallery — 10 tiles mixing categories (Zbraň, Zbroj,
     Lektvar, Šperk, Artefakt), 2 marked UNIKÁT, 1 marked LIMIT,
     various class-pip combos.
  2. Per-hra card-rows — 6 rows, one hovered with icons visible.
     Mix of IsSold (struck-through price), IsFindable, stock levels.
  3. Empty state with Drozd.
  4. Quick-peek popup — example "Meč elfů ze Stínového hvozdu",
     hero photo at 4:3 400px, type chip, 4 class pips (only Mage
     muted), effect paragraph.

ANTI-PATTERNS
  · No DxGrid in catalog mode.
  · No kingdom colors on ItemType chips (muted neutral only).
  · No emoji — Bootstrap Icons.
  · No photo cropping beyond object-fit:cover — photos are the
    artifact, don't mangle them.
```

---

## Builder addendum — Item grid

TARGET FILES
- Rewrite `src/OvcinaHra.Client/Pages/Items/ItemList.razor`
- New component: `src/OvcinaHra.Client/Components/ItemGalleryTile.razor` (catalog mode, 220×165 tile)
- New component: `src/OvcinaHra.Client/Components/ItemShopRow.razor` (per-hra mode, 96 px row)
- Theme additions: `.oh-it-*` scoped block in `ovcinahra-theme.css`

DATA BINDINGS
- Catalog: existing `GET /api/items` → `List<ItemListDto>`
- Per-hra: existing `GET /api/items/by-game/{gameId}` → `List<GameItemListDto>`
- No API changes expected

NOTES
- `ItemListDto.ImageUrl` is already populated by the backend (SAS URL) — use it directly on the tile/row; no separate ImagePicker fetch per item.
- ClassRequirements is already flattened as `ReqWarrior/ReqArcher/ReqMage/ReqThief` on the DTO — easy to light up the 4 pips.
- Consider extracting a `.oh-class-pips` component for reuse on ItemDetail's Karta and Quest reward displays.
