# Design Brief — Locations grid + quick-peek popup

**Status:** v1 · 2026-04-23
**Target surface:** Claude Design (claude.ai/design) — Ovčina org, design system already set up (`docs/design/CLAUDE-DESIGN-SETUP.md`)
**Implementation target:** `src/OvcinaHra.Client/Pages/Locations/LocationList.razor` + grid CSS additions to `ovcinahra-theme.css`

## Why this matters

Locations are the most-touched catalog in the app. Every organizer opens this grid multiple times per session during event prep. The current grid is functional but noisy — inline stash cards bleed flavor text, the context menu is discoverable only via three-dots, and variants are a wall of links rather than a hierarchy.

## Current state (summary)

- Dual-mode: **by-game** (default at `/locations`) vs **catalog** (`/locations?catalog=true`)
- Columns today: context menu · Name · Kind · Region · Parent/Variants · GamePotential · SetupNotes · *(by-game only)* Stashes inline
- Row click → `LocationEditPopup` (4-tab popup)
- Context-menu actions: GPS · Link · Map · Delete

## Scope of this brief

Grid **plus the quick-peek popup** that opens on row click. Full detail lives at `/locations/{id}` — a separate brief (`location-detail.md`).

## User decisions already locked

| Decision | Value |
|---|---|
| Detail split | Hybrid — grid popup is quick peek, full detail is a page |
| Parchment card treatment | Refine existing `.location-card-*` |
| Dual-mode grid | Keep both. Inline stash column stays (user can hide it) |
| Image aspect — main | **1:1** |
| Image aspect — placement | **4:3** landscape |
| Grid thumbnail | **Colored dot only** (kind-based marker color) — no thumbnail image |
| Stash tile inside grid cell | **MTG card size** (5:7 portrait) |
| Stash tile content | Image · Name · Treasure Quests listed below · **no descriptions** |
| Stash tile layout | **3 columns** on desktop, 2 tablet, 1 mobile |
| Variants | **Nested expansion** — click parent row expands to show variants inline |

## Data prereqs (already in entity, no new schema)

- `LocationKind` enum markers: `Town #2c3e50 · Village #27ae60 · Magical #8e44ad · Hobbit #f39c12 · Wilderness #16a085` (canonical, NOT kingdom colors)
- `Location.ParentLocationId` → parent/variant tree
- `LocationListDto.Stashes[].TreasureQuests[]` — what we render in the MTG-card tiles
- `SecretStash.ImagePath` — already supported by ImageEndpoints ("secretstashes" entity type)

## Prompt for Claude Design

Paste into a new project inside the **Ovčina** org. Design system (forest green, parchment, kingdoms, Drozd) is inherited automatically — no token boilerplate.

```
Design the Locations grid for OvčinaHra — the organizer-facing catalog and
per-game view of every location in the LARP world. This is the single most-
touched screen in the app.

AUDIENCE & DEVICE
Game organizers, 30–60, desktop first (1280–1600 wide) with 10" tablet as
secondary. Czech only, diacritics must render (č ř š ž ů ě).

DUAL MODE (URL query ?catalog=true toggles)

  Mode A — "by-game" (default, shown when an active game is selected)
    Toolbar: Hledat · Filtr kinds · Filtr region · Pouze s questy · Pouze s poklady
    Columns (left→right): context-menu dot · colored-dot marker · Název ·
      Druh (kind chip) · Oblast (region text) · Rodič/Varianty · Skrýše (rich) ·
      Questy (count badge) · GamePotential (muted) · SetupNotes (muted)

  Mode B — "catalog" (global, all locations, may not be in current game)
    Same columns except Skrýše is replaced by a single "Přiřadit ke hře" toggle
    per row (Ano/Ne badge + quick-assign button on hover)

MARKER DOT (leftmost visual cue, 10 px)
Use LocationKind marker colors — canonical, distinct from kingdom colors:
  Town        #2c3e50   dark slate
  Village     #27ae60   fresh green
  Magical     #8e44ad   violet
  Hobbit      #f39c12   amber
  Wilderness  #16a085   teal
Render as a plain colored circle; tooltip on hover with the kind's Czech
display name (Město / Vesnice / Kouzelné / Hobit / Pustina).

VARIANTS HIERARCHY
Parent rows show a disclosure chevron (▶) when `Variants.length > 0`. Click
expands in place: variant rows appear indented 24 px under the parent, same
columns, slightly dimmed. Parent row shows a "3 varianty" caption next to
its Name. Map pins only ever show parents — hint this in the expanded state
with a small `bi-geo-alt` icon next to the parent only.

STASH COLUMN (by-game mode) — THE REWORK
When a location has stashes, render them inside the Skrýše cell as a
3-column mini-gallery of Magic-The-Gathering-sized tiles.

Tile aspect ratio: 5:7 portrait (exactly 2.5 in × 3.5 in, the physical MTG
card size — ratio 0.714). Use "5:7" verbatim when wiring ImagePicker.
Display size in the grid: 70 × 98 px on desktop, 60 × 84 px on tablet.
Never crop or skew — the tile IS the card. Layout:

  [ tile | tile | tile ]
  [ tile ]
  …

Each tile contains:
  · stash image at 5:7 (ImagePicker / ImageUrlsDto.ImageUrl for "secretstashes")
  · stash name (2-line max, ellipsis; Merriweather 0.8125rem, forest green)
  · a thin hairline separator
  · bulleted list of that stash's TreasureQuests (title + small "Poklad" pill
    with muted amber bg); 3 max, "+ N více" if overflow
  · NO stash description text
Tiles use parchment-cream bg, burnt-orange 1px top border (the "ribbon"),
subtle green-tinted shadow, 6 px corner radius.
On hover: gentle lift + light sage-mist tint.
On tablet: 2 tiles per row. On mobile: 1 tile per row.

CONTEXT MENU (replaces current three-dots)
Move to row-hover: a subtle icon group that appears on the right edge of
the hovered row. Icons:
  · bi-geo-alt-fill   → "Nastavit GPS"
  · bi-link-45deg     → "Přiřadit quest"
  · bi-map-fill       → "Otevřít na mapě"
  · bi-trash-fill     → "Smazat"  (ghost-bordeaux)
Each icon 28×28, spaced 6 px, muted when row not hovered.

TOOLBAR (sticky above grid)
  [ Hledat… ]  [ Druh ▼ ]  [ Oblast ▼ ]  [ toggle: jen s poklady ]
  [ toggle: jen s questy ]                         [ + Nová lokace ]

EMPTY STATE
Use Drozd in the 120×120 state-illustration slot, copy:
  title: "Zatím tu není žádná lokace."
  cta:   "+ Nová lokace"

QUICK-PEEK POPUP (row click)
A 720×540 DxPopup showing ONLY the parchment card (read view). Content:
  · location-card header: Name (right-aligned Merriweather 2.2rem),
    Region · Druh (italic, muted)
  · 2-column body:
      left (42%): Description paragraph, Georgia serif, hyphens auto
      right (58%): main image at 1:1 (so the image slot is a SQUARE
      inside the 58% flex column — image is sized to fit the width of
      the column while maintaining 1:1, then centered)
  · Details block below with the numeric id badge (#42 in JetBrains Mono)
  · GamePotential callout (parchment-alt strip)
  · Variant links row (← parent, or → each variant)
  · Footer buttons: [Otevřít detail →] (primary, navigates to /locations/{id})
                    [Zavřít] (secondary)

STATES TO RENDER (stack vertically on faint parchment page bg)

  1. Grid by-game mode — 6 rows: one parent with 2 expanded variants, two
     with inline stash tiles (varying counts: 1, 3, 5-so-overflow), rest
     plain. Context icons visible on one hovered row.
  2. Grid catalog mode — 6 rows with per-row "Přiřazeno ke hře" Ano/Ne
     toggle and a hover-state "+ Přiřadit" button on the Ne rows.
  3. Empty state with Drozd.
  4. Quick-peek popup — example location "Esgarothský přístav", Town kind,
     Lake-people region, 4 paragraph Czech description, image, details,
     herní potenciál strip, one variant link.

DEVEXPRESS MAPPING (for the builder brief downstream)
  · DxGrid with DxGridDataColumn; OvcinaGrid (shared wrapper) keeps
    LayoutKey; bump to "grid-layout-locations-v3" on ship.
  · DxPopup for quick peek. DxFlyout (no popup) may be used for the hover
    icon group to avoid re-layout.
  · Stash tiles are a custom Razor component — ImagePicker for upload on
    the SecretStash side, but here we just render <img src="{url}">.
  · Marker dot is a 10 px span with background from a C# switch on
    LocationKind.

COPY DISCIPLINE
  · Everywhere: Czech verbatim above. Never Hrdina/Hrdinové here — this is
    the organizer's world-building view, entities are "lokace".
  · Don't romanticize: no "whispering woods", no marketing copy. Muted.

ANTI-PATTERNS
  · NO image thumbnails in the grid rows (colored dot only).
  · NO stash description text in the tiles.
  · NO kingdom colors on location markers (kinds have their own canon).
  · NO Drozd on populated screens (empty-state only here).
```

---

## Builder addendum — LocationList grid + quick-peek popup

TARGET FILES
- Razor: `src/OvcinaHra.Client/Pages/Locations/LocationList.razor`
- New component: `src/OvcinaHra.Client/Components/LocationStashTile.razor` (the MTG-card tile)
- Theme additions: `src/OvcinaHra.Client/wwwroot/css/ovcinahra-theme.css` (append `.oh-loc-*` scoped block)

GRID COLUMN CHANGES
- Add: marker dot column (40 px, first), variant disclosure chevron (on Name cell)
- Rework: Stashes column — now renders `LocationStashTile` in 3-col CSS grid
- Bump `LayoutKey` → `"grid-layout-locations-v3"` (MEMORY §L)
- Context menu → replace DxContextMenu with DxFlyout triggered on row hover icon group

DATA BINDINGS
- Marker color: static C# dictionary `LocationKindMarkerColors` (Town #2c3e50 etc.) — NEW constants class in `OvcinaHra.Shared/Domain/Constants`
- Stash tile image: GET `/api/images/secretstashes/{stash.Id}` → `ImageUrl`; cached per-tile

QUICK-PEEK POPUP
- 720×540 DxPopup
- Reuse existing `.location-card-*` CSS (already in theme, already good) but normalize image aspect-ratio via `aspect-ratio: 1/1` on the image container
- Footer "Otevřít detail →" calls `NavigationManager.NavigateTo($"/locations/{id}")`

OUT OF SCOPE
- Full edit form (lives on `/locations/{id}` page — see `location-detail.md`)
- GPS placement flow (unchanged — keep the existing GpsDialog component)
- Map pin styling (unchanged — MapPage uses its own marker system)

NOTES
- `LocationKindMarkerColors` is a new constants class — expose from Shared so both MapPage and LocationList can reference one source of truth. Consider also emitting CSS custom properties (`--oh-loc-town` etc.) if Claude Design suggests it.
- Respect MEMORY §D — all destructive icons route through DxPopup confirm.
