# Design Brief — Treasures planning dashboard `/treasures`

**Status:** v1 · 2026-04-24
**Target surface:** Claude Design (Ovčina org, design system inherited)
**Implementation target:** rewrite `src/OvcinaHra.Client/Pages/Treasures/TreasurePlanning.razor`

## Why it matters

Treasure planning is the **single most nuanced game-design act** an Ovčina organizer performs. You're not just CRUD-ing items — you're shaping pacing across the four acts of the game. Where does a Hrdina find their first prize? Where does the big climactic haul sit? How densely is the northern hvozd populated vs. the Lake-people's docks?

The current `/treasures` page exposes the primitives (pool · assign · overview grid) but not the **narrative shape** — organizers can't see at a glance how the map reads, or how the four stages balance. This redesign is a dashboard that makes distribution legible.

## Current state

- Existing `TreasurePlanning.razor` at `/treasures` — summary bar + pool DxGrid + all-quests overview DxGrid + edit popup. Functional, plain.
- `TreasureSummaryDto(PoolRemaining, Placed, StartCount, EarlyCount, MidgameCount, LategameCount)` — already exists.
- `TreasurePlanningLocationDto(LocationId, LocationName, LocationKind, StartCount, EarlyCount, MidgameCount, LategameCount, TotalItems, StashCount, MaxStashes, Stashes[])` — per-location stage breakdown, already in the API.
- `AssignTreasureDto` — one-shot assignment with title + difficulty + location/stash + items (picked from pool + unlimited items).

## Data prereqs — none new

Everything needed exists. The redesign is UX; the API already speaks the vocabulary.

## The four stages — canon

From `TreasureQuestDifficulty` enum:

| Enum value | Display (Czech) | Stage |
|---|---|---|
| `Start` | **Start** | Opening — Hrdinové are fresh, easy wins |
| `Early` | **Rozvoj hry** | Growth — exploration phase |
| `Midgame` | **Střed hry** | Heat — mid-game challenges |
| `Lategame` | **Závěr hry** | Finale — climactic stakes |

**Proposed stage palette** (parchment-compatible, not kingdom colors — new canon for stages):

| Stage | Hex | Role |
|---|---|---|
| Start | `#7FA657` sage | fresh, welcoming |
| Rozvoj hry | `#D4A63C` warm gold | growing |
| Střed hry | `#C17B3D` burnt orange | heat |
| Závěr hry | `#8C2423` deep crimson (= kingdom Azanulinbar-Dum by coincidence, accept the overlap) | climax |

Let designer ratify or propose alternatives — these must be distinguishable on the map and in small chips.

## User decisions already locked (from the conversation)

| Decision | Value |
|---|---|
| Map-based assignment | Must be possible — click a location/stash on the map to assign |
| Stage distribution view | Must be visible at a glance across all 4 stages |
| Location distribution | Must show how evenly (or not) locations hold treasures |
| Drag-and-drop from pool to map pin | Desirable if feasible |

## Prompt for Claude Design

Paste into a new project in the Ovčina org.

```
Design the Treasures planning dashboard at /treasures for OvčinaHra —
the single screen where organizers shape the narrative arc of a
game's loot. This is NOT a CRUD list — it's a pacing tool.

AUDIENCE & DEVICE
Veteran organizers planning a game, 2-3 months before the event.
Desktop-first (1440 nominal). Tablet secondary, but the primary
interaction is at a desk with a proper mouse.

PAGE ANATOMY — three vertical zones

  ZONE 1 (top, 160 px): Stage pipeline
  ZONE 2 (middle, flex): Map + assignment panel (two-column)
  ZONE 3 (bottom, collapsible): Overview of all treasure quests

============================================================
ZONE 1 — Stage pipeline
============================================================

Horizontal row of 4 big cards — one per stage: Start · Rozvoj hry ·
Střed hry · Závěr hry. Equal width. Each card shows:

  · Stage name (Merriweather 700, 1rem) — forest green
  · Big count numeral (JetBrains Mono, 2.5rem) — "12 pokladů"
  · Below: distribution indicator (a tiny bar of sub-cells: each
    cell = one treasure, colored by location kind or kingdom if
    wired). Rows with 20+ treasures wrap.
  · Small goal/target line if configured: "cíl 15" muted italic
  · Left-edge ribbon in the stage color (see palette above)

Color palette for stages (confirm or tweak):
  Start        #7FA657  sage
  Rozvoj hry   #D4A63C  warm gold
  Střed hry    #C17B3D  burnt orange
  Závěr hry    #8C2423  deep crimson

Right side of the pipeline row, after the 4 cards:
  · "Zásobník" (Pool) mini-card — 120 px wide. Shows PoolRemaining
    numeral + "předmětů čeká" line. Click → jumps focus to the Pool
    panel in Zone 2.

============================================================
ZONE 2 — Map + Assignment (two-column 60fr 40fr)
============================================================

LEFT (60fr): Map panel
  · Full-height MapLibre tile (height = viewport - Zone 1 - overview
    toggle row, min 520 px)
  · Stage filter chips above the map (sticky to map top):
      [VŠECHNY] [Start] [Rozvoj] [Střed] [Závěr]
    Multi-select. Inactive stages' wedges fade to 15% opacity on the
    map pins.
  · Each location with treasures renders a CUSTOM PIN — a 4-wedge
    mini-pie (NW/NE/SW/SE quadrants) colored by the four stages, each
    wedge sized proportionally to the treasure count at that stage.
    Empty stages render as muted parchment cream. Pin outer ring
    in burnt-orange, inner radius scales with total treasure count
    at that location (32 px min, 56 px max).
  · Locations with 0 treasures render a small 10 px muted dot with
    the LocationKind marker color (canon: Town #2c3e50, Village
    #27ae60, Magical #8e44ad, Hobbit #f39c12, Wilderness #16a085).
  · Hover pin → tooltip card:
      "{Location name}"
      4 stage rows: [wedge dot] Start 3 · Rozvoj 0 · Střed 2 · Závěr 1
      "Skrýše: 2/3 naplněny"
      "Klikni pro přiřazení"
  · Click pin → the right panel (Assignment) switches to "Location
    focus" mode for that location; see below.

  Map toolbar (under the map, 40 px strip):
    [ bi-zoom-in ] [ bi-zoom-out ] [ bi-crosshair Najít lokaci ]
    right-aligned: [ {N} lokací · {M} pokladů ] count

  The map interaction is the primary assignment affordance — organizer
  clicks a location, the right panel offers "drop a treasure here".

RIGHT (40fr): Assignment panel (switches by state)

  State A — Default (no location selected)
    Header: "Zásobník (N předmětů)"
    Filter row: [ Typ ▼ ] [ jen unikáty ] [ jen craftable ]
    Pool grid: 5:7 MTG tiles of pool items (`repeat(auto-fill,
    minmax(110px, 1fr))`). Each tile shows item image + name + count
    pill. Drag handle visible on hover (bi-grip-vertical).
    Drag a tile onto a map pin on the left → triggers Assignment
    dialog pre-filled with the picked item and the target location.

  State B — Location focus (pin clicked)
    Header: "{Location name}" + [ Zavřít ×]
    Pie indicator (30 px) showing current stage distribution.
    Tabs: [ Přehled ] [ Přidat poklad ]
    - Přehled: list of existing TreasureQuests at this location,
      grouped by stage. Each quest row: title + difficulty pill +
      item count + "Upravit" icon. Stashes are sub-listed with their
      own treasure counts.
    - Přidat poklad: form for creating a new TreasureQuest here —
      Title · Difficulty (4 stage chips to pick) · Target (Location
      direct OR one of this location's stashes, radio) · Items (pool
      picker with multiple select) · Clue (memo). "Uložit" POSTs
      AssignTreasureDto, closes back to Přehled, map pin updates.

  State C — Drag hover
    When dragging a pool tile over the map, the hovered pin grows
    slightly and shows a [+] overlay. Drop opens the "Přidat poklad"
    form pre-filled with the dragged item.

============================================================
ZONE 3 — Overview (collapsible, default collapsed)
============================================================

Collapsible strip: "Všechny poklady (N)" with a chevron. When
expanded, shows a OvcinaGrid with grouping by Difficulty (Start
group · Rozvoj group · Střed group · Závěr group) and columns:
  Título · Difficulty chip · Lokace/Skrýš · Items count · Actions
Row click → opens the same edit form as Zone 2 State B › Přidat
poklad, but with fields prefilled for editing.

============================================================
STATES TO RENDER (stack vertically for mockup)
============================================================

  1. Default empty game — all 4 stage cards showing 0, map with mostly
     small muted dots, empty pool, empty overview.
  2. Populated mid-plan — all 4 stage cards with varied counts
     (8/12/5/3), map with 15 pins of mixed pie compositions, pool
     with 6 items waiting, overview collapsed.
  3. Location focus state — Zone 2 right panel showing the Přehled
     tab for "Esgarothský přístav" with 3 existing quests (2 Start,
     1 Rozvoj), 2 stashes.
  4. Přidat poklad form in right panel — filling in a new Midgame
     quest at "Hvozd u Starého mostu".
  5. Dragging state — pool tile being dragged over a map pin with
     glow + [+] indicator visible.
  6. Overview expanded — the 4 grouped sections visible at the
     bottom with a couple of rows each.

ANTI-PATTERNS
  · No kingdom colors on stage chips — stages have their own palette.
  · No emoji.
  · No DxGrid in Zones 1 and 2 — those are custom layouts. DxGrid is
    fine for Zone 3's overview.
  · Don't treat map as decorative — it's the primary assignment
    affordance. Pin hit-targets must be ≥ 40 px at tablet size.
  · Don't put stage counts behind filter chips in Zone 1 — the 4
    cards show ALL stages at once, that's the value.
  · Don't collapse the pie-wedge pins into a single color — the
    distribution IS the information.

DESIGNER — PICK ONE IN THE MOCKUP
  A. Pin = 4-wedge pie with proportional wedges (my proposal).
  B. Pin = single dot colored by DOMINANT stage + small 4-dot
     sub-indicator row below.
  C. Pin = stacked numeric labels "3/0/2/1" in stage colors.
Commit to A, B, or C and label it in the mockup. I'll port the
choice straight through — don't render all three.

DEVEXPRESS MAPPING (for the builder)
  · MapLibre tile via existing map-interop.js — extend with a
    "addPieMarker(lat, lon, counts[4], sizes)" method
  · Zone 3 overview = OvcinaGrid with DxGrid (bump LayoutKey)
  · Pool tiles = custom CSS grid of draggable divs (HTML5 DnD)
  · DxPopup for the full-edit fallback when overview row clicked
```

---

## Builder addendum — Treasures planning

TARGET FILES
- Rewrite `src/OvcinaHra.Client/Pages/Treasures/TreasurePlanning.razor`
- New components:
  - `src/OvcinaHra.Client/Components/StagePipelineCard.razor` — one of the 4 Zone 1 cards
  - `src/OvcinaHra.Client/Components/TreasurePoolTile.razor` — draggable 5:7 tile in Zone 2 pool
  - `src/OvcinaHra.Client/Components/TreasureAssignForm.razor` — the Přidat poklad form (reused from overview edit)
  - `src/OvcinaHra.Client/Components/StageChip.razor` — small difficulty chip used everywhere
- Theme additions: `.oh-tp-*` (treasure-planning) scoped block in `ovcinahra-theme.css`
- **Extend** `src/OvcinaHra.Client/wwwroot/js/map-interop.js` with a pie-marker renderer: `addPieMarker(id, lat, lon, counts, colors, radius)` — 4 SVG wedges inside a group, appended to the MapLibre map as a custom HTML marker.

DATA BINDINGS
- GET `/api/treasures/summary?gameId={gid}` → `TreasureSummaryDto` (Zone 1)
- GET `/api/treasures/planning?gameId={gid}` → `List<TreasurePlanningLocationDto>` (per-pin data)
- GET `/api/treasures/pool?gameId={gid}` → `List<TreasurePoolItemDto>` (Zone 2 pool)
- POST `/api/treasure-quests` with `AssignTreasureDto` (Zone 2 Přidat poklad)
- PUT `/api/treasure-quests/{id}` for overview edit
- DELETE `/api/treasure-quests/{id}` — routes through DxPopup confirm (MEMORY §D)

DRAG-AND-DROP
- Use the native HTML5 `dragstart/dragover/drop` events on the pool tiles + map pins. Blazor supports this via `@ondragstart`/`@ondragover`/`@ondrop` event handlers.
- On `drop`, open the Assignment form pre-filled with the picked pool item + target location.

LAYOUTKEY
- Bump Zone 3's DxGrid LayoutKey to `grid-layout-treasures-v2` (the current one is `grid-layout-treasure-overview` — rename AND bump).

GOTCHAS
- **`TreasureQuest.LocationId` XOR `SecretStashId`** — API-side invariant. Validate in the Přidat poklad form: one radio "Na lokaci" vs "Ve skrýši", never both.
- **Pool item vs unlimited item** — `TreasureItem` can have `TreasureQuestId = null` (in pool) or set (placed). The pool shows items where `TreasureQuestId = null`. `UnlimitedItemAssignDto` lets the organizer place a "copy" (for consumables like potions) without consuming a pool entry. Keep both paths in the form.
- **Stage palette is NEW canon** — if the designer's mockup uses different hex than proposed, adopt the mockup's values; update CSS custom properties in theme.css:
  ```css
  --oh-stage-start:    #7FA657;
  --oh-stage-early:    #D4A63C;
  --oh-stage-midgame:  #C17B3D;
  --oh-stage-lategame: #8C2423;
  ```
- **MapLibre pie marker** — easiest path is an SVG custom marker: compute wedge `path d=...` from counts, set each wedge's `fill` from the stage palette. Keep the marker centered on `[lon, lat]` and anchor at center.
- **Overview grid LayoutKey bump** mandatory (MEMORY §L) — but if you keep field names identical, layout state maps cleanly.
- **Czech pluralization** — "pokladů" / "poklad" / "poklady" per 0/1/2-4/5+ rule. Reuse `PluralForm` helper pattern.

FOLLOW-UPS
1. **Playwright E2E** — drag a pool tile onto a pin, verify assignment popup opens prefilled.
2. **Goal/target lines in Zone 1 cards** — if the project wants to let organizers set per-stage target counts, add a `TreasurePlanningGoal` table. Out of scope for this port.
3. **Map-interop pie marker performance** — 100+ pins with 4 SVG wedges each may lag on low-end tablets. If measured, consider canvas-rendered markers or clustering at low zoom.
4. **Stash-level assignment from map** — clicking a pin currently selects the location; stash-level drill is in the right panel. A future iteration could let the organizer click a stash sub-pin directly. Not in MVP.
