# Design Brief — Location detail page `/locations/{id}`

**Status:** v1 · 2026-04-23
**Target surface:** Claude Design (Ovčina org)
**Implementation target:** new `src/OvcinaHra.Client/Pages/Locations/LocationDetail.razor` (fresh page, NOT a popup) + reuse/extend `.location-card-*` CSS

## Why a dedicated page

The current `LocationEditPopup` is 900 × 700 with 4 tabs. Organizers prepping an event spend minutes on a single location (reading lore, tweaking GamePotential, placing stashes). A popup forces tunnel-vision; a page gives them room and a shareable URL.

## Reference — current Upravit tab

The existing form (screenshot shared 2026-04-23) already renders Název · Typ · Oblast · GPS (with an "upravit na mapě" link) · Obrázek · Foto umístění · Info pro CP (NPC) · Co nachystat na lokaci · Rodič · Smazat/Zrušit/Uložit footer. Keep those labels verbatim. The redesign's job is to refine grouping (Identita · Vizuál · Obsah · Organizační sections), normalize the image aspect ratios (main 1:1, placement 4:3), and — per the designer's choice — decide where the mini-map lives.

## User decisions already locked

| Decision | Value |
|---|---|
| Parchment card | Refine existing `.location-card-*`, don't rebuild |
| Main image AR | **1:1** |
| Placement photo AR | **4:3** landscape |
| Variants | **Nested expansion** in the grid; on this page, shown as a variant-tree sidebar |
| Stashes | **MTG card size (5:7 portrait)**, 3-column tiles, name + treasure quests, NO descriptions |
| Mini-map | **Separate tab** |

## Page layout overview

Header (breadcrumb) → Title strip (Name, Region·Kind badge, action row) → **Tab strip** → Tab content below.

Tab strip (left-to-right): **Karta** · **Upravit** · **Lore** · **Skrýše** · *(optional)* **Mapa**

### Where does the mini-map live?

**Designer's call.** Two equally valid options — pick the one that reads cleaner in the mockup:

- **Option 1:** Dedicated **Mapa** tab (as originally briefed).
- **Option 2:** Inline mini-map panel on the **Upravit** tab, sitting beside the GPS row. The existing page already surfaces "upravit na mapě" next to coordinates — promote that hint into a small MapLibre preview (240×180 or similar), clickable to open `/map?focus={id}`. This keeps editing flow in one place and frees the tab strip.

The two options should not both appear. Pick one, label it clearly in the mockup, and drop the other section from the rendered output.

## Prompt for Claude Design

Paste into a new project in the **Ovčina** org (design system inherited).

```
Design the location detail page for OvčinaHra at URL /locations/{id}. This
is where organizers do deep editing on a single location between events.

AUDIENCE & DEVICE
Organizers, 30–60, desktop first (1440 wide nominal) with 10" tablet as
secondary. Czech only. Tabs are touch-targetable (≥ 44 px).

PAGE SKELETON

  Breadcrumb:  Lokace / {Name of current location}
  Title strip:
    [← zpět]  Lokace (h1, Merriweather)
    Subline:  Region · LocationKind chip · Parent link (if variant)
    Right-side actions: [Uložit] primary · [Zrušit] secondary · overflow
      menu (bi-three-dots-vertical): Smazat (ghost-bordeaux), Nastavit
      GPS, Přiřadit quest

  Tab strip (sticky below title):
    Karta · Upravit · Lore · Skrýše · [optional Mapa]
    Karta is default on page load.

  PLACEMENT OF THE MINI-MAP — DESIGNER'S CHOICE
  Two options — choose one and commit to it in the mockup:

    (A) A fifth tab "Mapa" containing the embedded MapLibre preview (as
        detailed in TAB 5 below).
    (B) An inline map panel embedded inside the Upravit tab, placed to the
        right of the GPS coordinate row (240–320 px square MapLibre preview
        with a single pin, "Otevřít v plné mapě" link beneath it). In this
        variant, DO NOT add a Mapa tab at all and skip TAB 5.

  The current app already surfaces "upravit na mapě" next to GPS — (B) is
  the natural promotion of that hint. Only render one option in the final
  mockup so the implementation path is unambiguous.

============================================================
TAB 1 — Karta (parchment card, read-mostly)
============================================================

Refined version of the existing .location-card treatment. Layout:

  Header row (right-aligned, serif):
    Name (Merriweather 900 2.4rem)
    "Region — Kind" italic muted line

  Body (CSS grid, 2 cols on ≥1024 px):
    Left col (42%): Description paragraph in Georgia serif,
      1.05rem, line-height 1.65, justified, hyphens auto.
    Right col (58%):
      - Main image at 1:1 (the container is flex-58%, image fills width,
        displayed as a SQUARE via aspect-ratio:1/1, object-fit:cover).
      - Below it, a small caption strip: "Znak lokace" (optional tag chips)

  Details block (below body, full width):
    numeric id badge (#42, JetBrains Mono, burnt-orange border chip)
    paragraph text (Details field)

  GamePotential strip:
    parchment-alt background, burnt-orange 3 px left border (ribbon),
    label "Herní potenciál:" bold, content inline.

  Variants footer (if any):
    "Varianty (3):" — horizontal list of chip-like links

DO NOT include any edit affordances on this tab. Read-only.

============================================================
TAB 2 — Upravit (edit form)
============================================================

DxFormLayout in two columns on desktop. Field order:

  Section: "Identita"
    Název (DxTextBox, required, full width)
    Druh (DxComboBox - LocationKind, with marker-dot prefix in each option)
    Oblast (DxTextBox, free-text autocomplete from existing regions)
    Rodič (DxComboBox of parents; EXCLUDE self + its descendants)

  Section: "Vizuál"
    Hlavní obrázek — ImagePicker EntityType="locations" field=default,
      AspectRatio="1:1", Width="280px"
    Fotka umístění — ImagePicker EntityType="locations" field="placement",
      AspectRatio="4:3", Width="360px"

  Section: "Obsah" (full width)
    Popis (DxMemo, Rows=4, NullText="Čtenářský popis lokace…")
    Detaily (DxMemo, Rows=5, NullText="Pravidlové poznámky, vazby…")
    Herní potenciál (DxMemo, Rows=3, NullText="Co lokace nabízí hrám…")

  Section: "Organizační"
    Přípravné poznámky (DxMemo, Rows=3)
    NPC info (DxMemo, Rows=3)
    Prompt (DxTextBox, italic hint: "volitelné — generátor scén")

  Footer: Uložit (primary) + Zrušit (secondary) fixed at page bottom
    during scroll; Smazat (ghost-bordeaux) far-left, only when editing
    existing record; Save is disabled until Název is non-empty.

============================================================
TAB 3 — Lore
============================================================

Two-pane read+edit for long-form world content.
  Left pane (60%): preview — Georgia serif, 1.1rem, line-height 1.7,
    muted parchment bg, hand-lettered feel.
  Right pane (40%): DxMemo 18 rows, writes to Location.Notes (or similar
    lore field — reuse existing field).
Toggle "Vzhled pergamenu" (preview) vs "Editor" on narrower screens.

============================================================
TAB 4 — Skrýše (reworked)
============================================================

Header: "Skrýše v této lokaci (N)" + button "+ Přidat skrýši" (opens
SecretStash picker).

Main grid of MTG-sized tiles.
Tile aspect ratio: 5:7 portrait (2.5 in × 3.5 in — the physical MTG card
size, ratio 0.714). Use "5:7" verbatim for ImagePicker. Display size
160 × 224 px on desktop (exactly 5:7), 120 × 168 px on tablet.

  3 columns desktop ≥ 1024 px
  2 columns tablet 768–1024 px
  1 column mobile < 768 px
  Row gap 24 px, column gap 20 px.

Each tile (composition, top to bottom):
  · Image area at 5:7 (ImagePicker read-only here — upload happens on the
    SecretStash page).
  · Stash name (Merriweather 700 1rem, forest green, 2-line ellipsis).
  · Hairline separator (1 px, burnt-orange 30% alpha).
  · TreasureQuest list: bullet of title + a small "Poklad" pill
    (muted amber bg #F9A825 20% + amber-dark border); 5 max, "+ N more"
    footer.
  · NO stash description text.
  · On hover: subtle lift (translateY -2 px) + sage-mist tint.
  · On click: navigates to /secret-stashes/{id}

Empty state: Drozd perch (48×48) + "Žádné skrýše zatím nejsou přiřazené."

============================================================
TAB 5 — Mapa (ONLY IF YOU PICKED OPTION A ABOVE)
============================================================

A 720 × 420 embedded MapLibre tile (same style as the main /map page)
centered on this location's GPS coordinates with the single pin placed
and colored by LocationKind marker color. No edit. Below the map:

  · "GPS: 50.0832°, 14.4336°" (JetBrains Mono)
  · [Otevřít v plné mapě →] (primary button, links to /map?focus={id})
  · If no GPS set: parchment empty state with Drozd perch + copy
    "Lokace ještě nemá GPS. Nastav ji přes Upravit › Organizační.".

If you picked Option B (inline map on Upravit), drop this tab entirely —
no placeholder, no stub. Just end the tab strip at Skrýše.

============================================================
STATES TO RENDER (stack vertically for the mockup)
============================================================

  1. Tab=Karta — location "Esgaroth" populated with a 4-para description,
     1:1 image of a village sketch, details id #08, GamePotential strip,
     no variants.
  2. Tab=Upravit — same location, form fields populated, image picker
     slots show current images.
  3. Tab=Skrýše — 5 stashes shown as 3-col MTG tiles (row 1: 3 tiles,
     row 2: 2 tiles), one tile has overflow "+ 3 more".
  4. Tab=Mapa — mini-map with single pin; below it GPS + "Otevřít v plné
     mapě" button.
  5. Tab=Karta empty state (location with no description/images yet) —
     Drozd slot in the right column asking "Napiš první popis této lokace."

ANTI-PATTERNS
  · No kingdom colors on LocationKind chips (kinds have their own palette).
  · No Drozd on populated Karta / Upravit / Skrýše / Mapa tabs.
  · No 16:9 cinematic main image — we locked 1:1.
  · No stash descriptions anywhere.
```

---

## Builder addendum — LocationDetail page

TARGET FILES
- New page: `src/OvcinaHra.Client/Pages/Locations/LocationDetail.razor` (route `/locations/{id:int}`)
- Existing popup `src/OvcinaHra.Client/Components/LocationEditPopup.razor` → repurpose Karta tab as the quick-peek popup (per `location-list.md`); the remaining tabs (Upravit, Lore, Skrýše, Mapa) move to the new page.
- New component: `src/OvcinaHra.Client/Components/LocationStashTile.razor` (shared with grid's rework)
- Theme: extend `.location-card-*` (already in `ovcinahra-theme.css`) for the 1:1 image constraint; append `.oh-loc-detail-*` scoped block for tab-specific styling

DATA BINDINGS
- Page loads `GET /api/locations/{id}?gameId={contextGameId}` on route change
- Karta uses existing `LocationListDto` fields — no new DTO needed
- Skrýše tab uses `LocationListDto.Stashes[]` already present
- Mini-map tab calls `map-interop.js` with `LocationListDto.Coordinates` (GpsCoordinates value object already projects to the DTO via existing serialization)
- Edit form saves via existing `PUT /api/locations/{id}` endpoint — no new endpoint needed

NAVIGATION FLOW
- Grid quick-peek popup → "Otevřít detail →" → `NavigationManager.NavigateTo($"/locations/{id}")`
- Mini-map tab → "Otevřít v plné mapě" → `NavigationManager.NavigateTo($"/map?focus={id}")`
- Parent/variant links stay in-app (NavigationManager, no full reload)

OUT OF SCOPE
- GPS placement (keep existing GpsDialog flow, exposed via overflow menu)
- Stash editing (still lives on /secret-stashes)
- Map pin layer changes (MapPage owns that)

NOTES
- This page uses tabs heavily — use `DxTabs`; each tab's content should NOT pre-render (lazy render) to keep first paint cheap.
- Mini-map tab uses MapLibre; ensure `map-interop.js` supports single-pin read-only mode.
- Tile image URLs: GET `/api/images/secretstashes/{stash.Id}` returns `ImageUrl`; the LocationStashTile component fetches once per tile with `OnParametersSetAsync` + local cache.
- MEMORY §D: Smazat always goes through DxPopup confirm.
- MEMORY §L: this page uses no DxGrid, so no LayoutKey concerns.
