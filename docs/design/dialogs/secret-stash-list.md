# Design Brief — SecretStash grid `/secret-stashes`

**Status:** v1 · 2026-04-23
**Target surface:** Claude Design (Ovčina org, design system inherited)
**Implementation target:** rewrite `src/OvcinaHra.Client/Pages/SecretStashes/SecretStashList.razor`

## Why it matters

Stashes are small but numerous (3 max per Location per Game, but dozens catalog-wide). The current list is a plain DxGrid with Name/Description — organizers can't see at a glance which stash is which, which are under-used, or which have no treasures assigned.

## Current state

- Dual mode: `/secret-stashes` (per-hra) vs `?catalog=true` (global)
- Columns: Name · Description · (catalog) Přiřadit/Odebrat button
- Row click opens a small edit popup
- No visual identity — stashes look identical in the list

## User decisions already locked

| Decision | Value |
|---|---|
| Dual mode | Keep both |
| Primary visual | **Gallery of MTG tiles** (not a traditional grid) — 5:7 portrait cards matching LocationStashTile |
| Tile size | Small: 120 × 168 px desktop (6–8 cols), 96 × 134 px tablet (3 cols), 2 cols mobile |
| Card content | Image · Name · small count badges (treasures / games-using) |
| Catalog-mode toggle | Per-tile "Přiřadit ke hře" pill button overlaid |
| Row/tile click | Navigate straight to `/secret-stashes/{id}` — tile IS the peek |

## Prompt for Claude Design

```
Design the SecretStash catalog grid for OvčinaHra — organizer view of all
hidden caches in the world. This replaces a generic table with a
Magic-The-Gathering-style card gallery matching the tiles on
LocationDetail › Skrýše.

AUDIENCE & DEVICE
Organizers, desktop-first, 10" tablet secondary.

DUAL MODE (?catalog=true toggles)

  Mode A — "Pro hru" (default)
    Shows only stashes assigned to the currently-active game.
    Small caption on each tile: "v N lokacích" (number of locations
    using this stash in this game).

  Mode B — "Katalog"
    All stashes across history. Each tile has an overlay pill:
    "Přiřadit ke hře +" (if not in current game) or "V této hře ✓"
    (greyed, unclickable — edit from per-hra mode).

LAYOUT

Title strip: "Tajné skrýše" h1 · mode toggle (Pro hru / Katalog) ·
"+ Nová skrýš" primary.

Toolbar row:
  [ Hledat… ]  [ jen s poklady ]  [ jen bez lokace ]       [ N skrýší ]

Gallery: responsive CSS grid of MTG tiles.
  · Desktop (≥1280px): 8 columns of 120 px
  · Wide desktop (1024-1280): 6 columns of 110 px
  · Tablet (768-1024): 3 columns
  · Mobile (<768): 2 columns
  · Gap: 20 px

TILE COMPOSITION (5:7 portrait, 120 × 168 px desktop)
  · Image at 5:7 top, object-fit:cover, fallback gradient
  · Name (Merriweather 700, 0.875rem, forest green, 2-line clamp)
  · Below name: 2 micro-chips
      "◆ 3 poklady" (mono, amber background)
      "⌘ 2 lokace"   (mono, sage background)
    Replace the glyphs with bi-star-fill and bi-geo-alt-fill
    respectively — no emoji.
  · On hover: lift -2 px, sage-mist tint, shadow deepens
  · On catalog mode + not assigned: bottom overlay pill "+ Přiřadit ke hře"
    (forest green bg, white text, fades in on hover)
  · On catalog mode + assigned: muted "✓ v této hře" pill top-right

EMPTY STATE (per-hra, no assignments)
Drozd 120×120 + "Žádné skrýše přiřazeny k této hře." +
[Otevřít katalog] secondary link + [+ Nová skrýš] primary.

CLICK BEHAVIOUR
Tile click → navigates to /secret-stashes/{id} (full detail page).
No quick-peek popup — the tile IS the peek.

NEW-STASH MINI-POPUP
+ Nová skrýš opens a small DxPopup with three fields — Název (required),
Popis (memo), Obrázek (ImagePicker 5:7 × 200 px). Footer CTA is
"Uložit a otevřít" — POSTs and navigates to /secret-stashes/{newId}.

STATES TO RENDER (stack vertically for mockup)

  1. Per-hra, 8 stashes populated, 2 with zero treasures (visibly
     dimmer + "0 pokladů" chip turns muted grey).
  2. Catalog mode showing 8 stashes; 3 already in current game
     (checkmark pills) and 5 not (hover pills visible on one).
  3. Empty state.
  4. New-stash mini-popup.

ANTI-PATTERNS
  · No DxGrid (this page replaces a table with a gallery).
  · No emoji.
  · No kingdom colors on the chips — stash tiles use neutral
    parchment + amber Poklad accent.
  · No description preview on tiles — name + chips only.
```

---

## Builder addendum — SecretStash grid

TARGET FILES
- Rewrite `src/OvcinaHra.Client/Pages/SecretStashes/SecretStashList.razor` — drop DxGrid, use CSS grid of tiles
- New component: `src/OvcinaHra.Client/Components/SecretStashGridTile.razor` — reuses `.oh-ss-card-*` styling from the detail page brief
- Theme additions: append `.oh-ss-grid-*` rules
- New small popup: `Components/NewSecretStashPopup.razor` — Name + Description + ImagePicker, then POST + navigate

DATA BINDINGS
- GET `/api/secret-stashes?gameId={gid}&mode={pro-hru|catalog}`
  If the endpoint doesn't support query filters yet, extend it
- Tile click → `NavigationManager.NavigateTo($"/secret-stashes/{id}")`
- "+ Přiřadit ke hře" pill on catalog tile → POSTs `GameSecretStash { gameId, stashId }` with a quick location-picker flyout (or navigates to /secret-stashes/{id} › Výskyt tab)

NOTES
- LocationStashTile and this grid's tile both render 5:7 MTG tiles — extract `.oh-mtg-tile-*` common rules if the duplication hurts.
- Dropping DxGrid means losing its LayoutKey persistence — not a problem for a gallery, but call it out in the PR.
