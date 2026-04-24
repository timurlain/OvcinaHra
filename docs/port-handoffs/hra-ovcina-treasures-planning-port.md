# Handoff — port the Treasures planning dashboard (2026-04-24)

Load the `hra-ovcina-tinkerer` skill first. Then read this file and `docs/design/dialogs/treasures-planning.md` (the designer brief) before touching code.

## What this is

Claude Design shipped the mockup for `/treasures` — a three-zone planning dashboard replacing the current summary + pool + overview page. Organizers shape game pacing by placing treasures into the four acts (Start / Rozvoj hry / Střed hry / Závěr hry). The map is the primary assignment affordance.

- **Bundled mockup:** `C:\Users\TomášPajonk\Downloads\Poklady - Pl_nov_n_ _standalone_.html` (1.43 MB, gitignored when copied in).
- **Decoded HTML:** copy the bundled file into `docs/design/dialogs/treasures-planning.mockup.html` (already gitignored) and decode via:
  ```python
  import re, json
  with open("docs/design/dialogs/treasures-planning.mockup.html", encoding="utf-8") as f: h = f.read()
  with open("docs/design/dialogs/treasures-planning.mockup.unbundled.html", "w", encoding="utf-8") as o:
      o.write(json.loads(re.search(r'<script type="__bundler/template"[^>]*>(.*?)</script>', h, re.S).group(1).strip()))
  ```
- Designer notes at the bottom of the decoded file (h4 sections): *Pin = pacing v jednom prvku*, *Etapová paleta — nová kanonika*, *Drag-and-drop*.

## Decisions the designer locked

| Area | Choice |
|---|---|
| Pin variant | **A — 4-wedge pie pin** (locked). The mockup renders A/B/C side-by-side for comparison, then commits to A in the main states. CSS comment in the mockup: `/* Pie-wedge pin (A) */`. |
| Stage palette | **Kanon** (the hex values from the brief) — `#7FA657 / #D4A63C / #C17B3D / #8C2423`. Designer surfaced 2 alternative palettes ("Plum závěr", "Zemité") as a future "Přizpůsobit" feature — **out of scope for this port**. |
| Zone 1 layout | `grid-template-columns: 1fr 1fr 1fr 1fr 160px` — 4 stage cards + Zásobník mini-card |
| Zone 2 layout | `grid-template-columns: 60fr 40fr` — Map left, Assignment panel right |
| Pool tile grid | `repeat(auto-fill, minmax(110px, 1fr))` — tiles are 5:7 MTG aspect |
| Pool smaller grid (compact) | `repeat(auto-fill, minmax(84px, 1fr))` — used in the location-focus state's "existing quests" mini-tiles |
| Zone 3 | Collapsible — renders in both "Sbalený" and "Rozbalený" states in the mockup. Expanded shows quests grouped by stage. |
| Nekonečný items | Preserved — footer hint: *"Také můžeš umístit „nekonečný" předmět (lektvar, mince) bez spotřeby z poolu."* The form needs a toggle for "Nekonečný předmět" path that skips pool consumption. |
| Pin hover | Grows + drop shadow intensifies (`.pin-pie:hover{transform:scale(1.06); filter:drop-shadow(0 4px 8px rgba(40,25,5,.5));}`). `z-index` bumps on hover. |

## The 6 rendered states (confirm with the mockup)

1. **Rozehrané plánování** — 28 pokladů, 6 v zásobníku. Full populated dashboard.
2. **Prázdná hra** — empty state. All 4 stage cards show 0, map mostly muted dots, empty pool + Drozd.
3. **Ohnisko lokace** — "Esgarothský přístav" clicked. Assignment panel switches to the Location focus view with Přehled tab listing existing quests + Přidat poklad tab.
4. **Formulář Přidat poklad** — "Hvozd u Starého mostu" as target. Full form: title · stage chips · target radio (location vs stash) · items picker · clue · "Nekonečný" path.
5. **Přetažení** — drag state. Pool tile hovering over "Trpasličí brána" pin with `[+]` overlay + glow.
6. **Rozbalený přehled** — Zone 3 expanded. Quests grouped by stage (Start group · Rozvoj group · Střed group · Závěr group).

## Class prefixes in the mockup

Flat names. Scope under `.oh-tp-*` (treasure planning) on port:

| Mockup | → Ported |
|---|---|
| `st-*` (stage strip / card) | `.oh-tp-stage-*` |
| `k-*` (knob/kingdom-badge-like chips) | `.oh-tp-k-*` |
| `wedge-*` | `.oh-tp-wedge-*` |
| `num-*` (big numerals) | `.oh-tp-num-*` |
| `pin-*`, `pin-pie` | `.oh-tp-pin`, `.oh-tp-pin-pie` |
| `ring` / `ringInner` | `.oh-tp-ring` / `.oh-tp-ring-inner` (concentric circles) |
| `pool-*` | `.oh-tp-pool-*` |
| `map-*` | `.oh-tp-map-*` |
| `stage` chip | use shared `.oh-stage-chip-*` if extracted, else `.oh-tp-stage-chip` |
| `hdr`/`sub`/`sw` (mini layout utilities) | fold into `.oh-tp-*` namespace |

## Pin-pie rendering — C# + SVG path

The pie pin is an SVG custom marker with 4 quadrant wedges. Each wedge is an SVG `path` computed from the count for its stage. Centered on `[lon, lat]`. Example wedge math (clockwise, starting 12 o'clock):

- Stage 0 (Start) → top-right quadrant, proportional arc from 0° to `360 * (start / total) / 4 * X` — clamp to quadrant share
- Repeat for other stages.

**Simpler approach — fixed quadrants, wedge radius scales with proportion:**
- Each stage occupies exactly one quadrant (90°)
- Wedge radius = `maxR * min(1, count / target)` — unfilled portion is muted parchment
- Gives a visual "fullness" per stage without arc-math complexity

**Recommended:** go with fixed-quadrant + radius scaling. Simpler to implement, reads clearly on the map. Add proportional-arc as a follow-up if organizers want it.

Extend `wwwroot/js/map-interop.js` with a method:
```js
addPieMarker(id, lat, lon, counts /* [start, early, mid, late] */, maxCount, totalRadius)
```
Returns an HTML element anchored at center, containing one `<svg>` with 4 wedge paths. Store references so the marker can be removed/updated on state changes.

## Starting state

### Branches
- `main` at v0.12.1 (or higher by the time you start)
- **Your new branch:** `feat/treasures-planning-port` off latest `main`

### Files to touch (this branch)

- **Rewrite** `src/OvcinaHra.Client/Pages/Treasures/TreasurePlanning.razor`
- **New** `src/OvcinaHra.Client/Components/StagePipelineCard.razor` — one of 4 Zone 1 cards (count numeral, distribution bar, optional goal line)
- **New** `src/OvcinaHra.Client/Components/TreasurePoolTile.razor` — draggable 5:7 tile in the pool
- **New** `src/OvcinaHra.Client/Components/TreasureAssignForm.razor` — the Přidat poklad form (reused by overview edit)
- **New** `src/OvcinaHra.Client/Components/StageChip.razor` — small difficulty pill (reusable across the app — flag in PR)
- **Append** `.oh-tp-*` block to `src/OvcinaHra.Client/wwwroot/css/ovcinahra-theme.css`
- **Extend** `src/OvcinaHra.Client/wwwroot/js/map-interop.js` with `addPieMarker` + `removePieMarker` + `updatePieMarkerCounts`

### Stage palette CSS custom properties (add to theme.css `:root`)

```css
--oh-stage-start:    #7FA657;   /* sage */
--oh-stage-early:    #D4A63C;   /* warm gold */
--oh-stage-midgame:  #C17B3D;   /* burnt orange */
--oh-stage-lategame: #8C2423;   /* deep crimson */
```

These are user-approved canon (2026-04-24). See `hra-ovcina-designer` skill reference `stage-palette.md` for the full spec.

## Acceptance criteria

- [ ] `dotnet build OvcinaHra.slnx` clean, zero new warnings (`TreatWarningsAsErrors=true`)
- [ ] `dotnet test` green
- [ ] `/treasures` renders Zone 1 row of 4 stage cards + Zásobník with correct counts from `TreasureSummaryDto`
- [ ] Map (Zone 2 left) shows a 4-wedge pie pin per location with treasures; wedge radius scales with per-stage count
- [ ] Locations without treasures render small 10 px muted dots with `LocationKind` marker color (canon)
- [ ] Stage filter chips above the map multi-select; inactive stages' wedges fade to 15% opacity
- [ ] Assignment panel (Zone 2 right) in 3 states: Default (pool grid), Location focus (Přehled + Přidat poklad tabs), Drag hover (+ overlay on target pin)
- [ ] Drag-and-drop: drag pool tile over pin → drop opens Přidat poklad form prefilled with item + target location
- [ ] "Nekonečný předmět" path in the form: toggle skips pool consumption, POSTs with `UnlimitedItems` instead of `TreasureItemIds`
- [ ] Zone 3 collapsible — expanded shows quests grouped by Difficulty with edit on row click
- [ ] Delete (overview row context menu) routes through DxPopup confirm (MEMORY §D)
- [ ] Stage palette CSS custom properties added to `:root` in `ovcinahra-theme.css`
- [ ] LayoutKey bumped on Zone 3 grid: `grid-layout-treasure-overview` → `grid-layout-treasures-v2`
- [ ] Czech diacritics render throughout

## Testing expectations

- **Integration tests:** verify the planning endpoints exist and return expected shapes:
  - `GET /api/treasures/summary?gameId={gid}` → `TreasureSummaryDto`
  - `GET /api/treasures/planning?gameId={gid}` → `List<TreasurePlanningLocationDto>`
  - `GET /api/treasures/pool?gameId={gid}` → `List<TreasurePoolItemDto>`
  - `POST /api/treasure-quests` with `AssignTreasureDto` covers both `TreasureItemIds` (pool consumption) and `UnlimitedItems` paths
- **Playwright E2E:** drag a pool tile onto a pin → verify Přidat poklad form opens prefilled with the dragged item and correct target location → submit → verify pin wedges update.

## PR workflow

```
git checkout -b feat/treasures-planning-port main
# ... implement ...
dotnet build OvcinaHra.slnx
dotnet test
git add -p && git commit   # [v0.12.x] per skill
git push -u origin feat/treasures-planning-port
gh pr create --base main --head feat/treasures-planning-port \
  --title "feat(treasures): planning dashboard — stage pipeline + pie-pin map + drag-drop [v0.12.x]" \
  --body "Ports the treasures planning mockup. Three-zone dashboard replacing current summary+pool+overview. Extends map-interop.js with pie-wedge markers. Adds stage palette custom properties (user-approved canon). Keeps Nekonečný item path. LayoutKey bumped. See docs/design/dialogs/treasures-planning.md + docs/port-handoffs/hra-ovcina-treasures-planning-port.md for decisions."
```

Squash-merge with `gh pr merge <N> --squash --delete-branch` after CI + Copilot. Verify deploy via `curl https://api.hra.ovcina.cz/api/version`.

## Gotchas

- **`TreasureQuest.LocationId` XOR `SecretStashId`** — API-side invariant. Validate in Přidat poklad form: one radio "Na lokaci" vs "Ve skrýši", never both.
- **Pool vs Nekonečný** — `TreasureItem` with `TreasureQuestId = null` is in the pool. Placing a pool item sets its `TreasureQuestId` (consumed). "Nekonečný" uses `UnlimitedItemAssignDto` which creates new `TreasureItem` rows bound to the quest without touching the pool.
- **Pin math** — fixed-quadrant + radius-scaling is simpler than proportional-arc and reads better at small sizes. Don't chase mathematical perfection; this is a glance visualization.
- **Drag cross-frame** — native HTML5 DnD between a Blazor component and a MapLibre canvas is tricky. You'll likely need `dragover preventDefault` on a transparent overlay div that covers the map while dragging, then hit-test pins via MapLibre's queryRenderedFeatures on drop. Reference the monster-list port's hover pattern for how Blazor handles native events.
- **Stage colors ≠ Kingdom colors** — palettes stay distinct. Stage chips never use kingdom hex, and vice versa. `#8C2423` (Závěr hry) coincides with Azanulinbar-Dum red by context — accept the overlap; contexts don't mix.
- **LayoutKey bump** on Zone 3 DxGrid is mandatory (MEMORY §L).
- **"Přizpůsobit" palette picker** shown in the mockup is **out of scope** for this port. Open a follow-up issue if the user wants it. Hard-wire to "Kanon" palette values.
- **`TreasureSummaryDto` field names** — `StartCount / EarlyCount / MidgameCount / LategameCount` on the DTO map to stages Start / Rozvoj hry / Střed hry / Závěr hry respectively. Don't rename in the UI.
- **Czech pluralization** for "pokladů" — "1 poklad" / "2-4 poklady" / "5+ pokladů". Reuse the project's `PluralForm` helper.

## Follow-ups

1. **Playwright E2E** — drag-drop + form-submit flow + pin-wedge-update verification.
2. **Palette "Přizpůsobit" picker** — the designer's alternative palettes ("Plum závěr", "Zemité", "Tweaks") as an organizer-selectable preference. Separate PR.
3. **Proportional-arc pie pins** — if the fixed-quadrant approximation reads imprecisely at high treasure counts, revisit with proper arc math.
4. **Stash-level assignment from map** — currently click selects location; stash picker is in the right-panel form. A future iteration could let the organizer click a stash sub-pin directly on the map. Not in MVP.
5. **Stage palette canon in theme.css** — if future features need the stage colors (e.g. monster detail Kořist tab tagging by stage), extract the 4 CSS custom properties to a shared block.

## Companion references

- Brief: `docs/design/dialogs/treasures-planning.md`
- Stage palette canon: `~/.claude/skills/hra-ovcina-designer/references/stage-palette.md` (or wherever the skill lives)
- Design canon running log: `~/.claude/skills/hra-ovcina-designer/references/design-canon.md`
- MapLibre precedents: `docs/port-handoffs/hra-ovcina-location-detail-port.md` — inline mini-map uses a separate `ovcinaMiniMap` helper; pie markers extend the primary `ovcinaMap`
- Prior drag-and-drop precedent: none yet — this port establishes the pattern
- Builder template: `docs/design/BUILDER-BRIEF-TEMPLATE.md`
