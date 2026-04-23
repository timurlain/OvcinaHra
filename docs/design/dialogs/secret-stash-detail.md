# Design Brief — SecretStash detail `/secret-stashes/{id}`

**Status:** v1 · 2026-04-23
**Target surface:** Claude Design (Ovčina org, design system inherited)
**Implementation target:** new `src/OvcinaHra.Client/Pages/SecretStashes/SecretStashDetail.razor`

## Why it matters

`LocationStashTile` (shipped in LocationDetail) already links to `/secret-stashes/{id}`. That page needs to exist and visually continue the MTG-card aesthetic — a stash opens from a tiny MTG tile on a Location page, it should read as the **same artifact blown up to card size**.

## Current state

- `SecretStashList.razor` at `/secret-stashes` — DxGrid with Name/Description columns, dual mode (per-hra vs catalog), `+ Nová skrýš` popup
- No dedicated detail page
- Entity: `SecretStash { Id, Name, Description, ImagePath }`
- Relationships:
  - `GameSecretStash` — per-game assignment (binds stash to a Location in a given Game, with 3-per-location CHECK constraint)
  - `TreasureQuest` — what's hidden in the stash (list of titles + reward hints)

## Data prereqs — none

No schema changes. Everything needed is already on the entity + existing DTOs (`SecretStashListDto`, `SecretStashDetailDto`).

## Prompt for Claude Design

Paste into a new project in the Ovčina org.

```
Design the SecretStash detail page at /secret-stashes/{id:int} for
OvčinaHra — the organizer view of a single hidden cache in the LARP world.

AUDIENCE & DEVICE
Organizers, 30–60, desktop first (1440 nominal), 10" tablet secondary.
Czech only, diacritics must render.

CONTEXT (READ THIS FIRST)
This page opens from clicking a Magic-The-Gathering-sized tile on the
LocationDetail › Skrýše tab. Visually it should feel like "the same
artifact, blown up". Same parchment warmth, same 5:7 portrait image,
same Poklad pill, same restraint on decoration.

PAGE SKELETON

  Breadcrumb:  Tajné skrýše / {Stash Name}
  Title strip (sticky):
    [← zpět]  {Stash Name} (h1, Merriweather)
    Subline: "tajná skrýš" italic muted + "v N hrách" count badge
    Right-side actions:
      Read mode: [Upravit] secondary · [Smazat] ghost-bordeaux
      Edit mode: [Zrušit] secondary · [Uložit] primary

  Tab strip (sticky under title):
    Karta · Upravit · Poklady · Výskyt
    Default: Karta.

============================================================
TAB 1 — Karta (the MTG card, large)
============================================================

A single large MTG tile (5:7, 2.5 × 3.5 in physical ratio, displayed
at 320 × 448 px on desktop) centered in the content column with some
parchment breathing room around it. The card composition (top → bottom):

  · Image area at 5:7 (aspect-ratio:5/7, object-fit:cover) with a dark
    parchment-brown gradient fallback when empty
  · Bottom-of-image overlay: stash id chip top-left (#42 in JetBrains
    Mono, dark translucent bg), 5:7 tag bottom-right
  · Card body (parchment cream, 16 px padding):
      - Name in Merriweather 700, forest green, 1.5rem
      - Hairline burnt-orange separator
      - Description paragraph — Georgia serif 1rem, line-height 1.55,
        max 4 lines before "…" with a "Zobrazit celý popis" toggle
      - Treasure count: "3 poklady ukryté zde" in mono muted

To the right of the card (or below on narrower): "Odhalené 2× / Zatím
neodhalené" — small organizer-note area. If none: empty.

============================================================
TAB 2 — Upravit
============================================================

DxFormLayout:
  Název (DxTextBox, required, full width)
  ImagePicker — EntityType="secretstashes", AspectRatio="5:7",
    Width="260px". Centered in the form column.
  Popis (DxMemo, Rows=5, NullText="Co je to za místo, jak je skrýš
    utajená, co v ní může být…")

Below the form:
  Footer: Uložit (primary) / Zrušit (secondary); Smazat (ghost-
  bordeaux) far-left, only when editing existing.

NOTE: per MEMORY feedback_ovcina_card_text_sacred, do not propose
changes to existing stash card templates without user approval — this
form is for metadata only, never for the text of printed cards.

============================================================
TAB 3 — Poklady (treasure quests assigned to this stash)
============================================================

Grid of mini-cards, one per TreasureQuest bound to this stash. Each
card (desktop 260 × 120 px, tablet full-width):

  · Title (Merriweather 600, forest green)
  · Difficulty badge (Start / Rozvoj hry / Střed hry / Závěr hry —
    colored pill, existing canon)
  · Reward summary: "💰 N grošů · ⚔ N XP" in mono — NO emoji, use
    bi-currency-exchange + bi-star instead
  · "Upravit" icon button → navigates to /treasures/{id}

Header row: "Poklady ukryté zde (N)" + [+ Přidat poklad] primary
button. Empty state: Drozd perch + "Tato skrýš zatím nic neukrývá."

============================================================
TAB 4 — Výskyt (where this stash appears across games)
============================================================

Table of GameSecretStash rows:
  | Hra | Lokace | Přiřazeno | Odebrat |
Sorted by game start date descending. Click on a row → navigates to
that Location's detail page. "Odebrat" opens a DxPopup confirm (per
MEMORY §D).

If stash has no assignments: empty state with "+ Přiřadit ke hře"
button opening a tiny popup (game picker + location picker).

============================================================
STATES TO RENDER (stack vertically for mockup)
============================================================

  1. Karta — populated. Example: "Hadr pod kamenem u studny",
     description 2 paragraphs, treasure count "2 poklady".
  2. Upravit — same stash, form populated.
  3. Poklady — 4 treasure-quest mini-cards (one "+ Přidat" slot).
  4. Výskyt — 3 assignments across different games.
  5. Karta — empty state (no description, no image). Drozd perch,
     "Napiš první popis této skrýše."

ANTI-PATTERNS
  · No emoji (bi-* icons only, per hra-ovcina-tinkerer skill).
  · No kingdom colors on tab chips.
  · No Drozd on populated Karta / Upravit.
  · No text modifications to the 3 printed card templates — those are
    sacred (MEMORY feedback_ovcina_card_text_sacred). This is metadata.
  · Card image must render at exact 5:7 — never crop or skew.

DEVEXPRESS MAPPING (for the builder)
  · DxGrid not needed on this page. DxFormLayout for Upravit.
  · Poklady grid is a custom CSS grid of mini-cards (NOT DxGrid).
  · Výskyt can be a plain table or DxGrid — designer's call.
```

---

## Builder addendum — SecretStash detail

TARGET FILES
- New page: `src/OvcinaHra.Client/Pages/SecretStashes/SecretStashDetail.razor` (route `/secret-stashes/{id:int}`)
- Theme additions: append `.oh-ss-*` scoped block to `ovcinahra-theme.css`
- Optional new component: `src/OvcinaHra.Client/Components/TreasureMiniCard.razor` for the Poklady tab

DATA BINDINGS
- Page loads `GET /api/secret-stashes/{id}` (verify endpoint exists; add if needed)
- Výskyt tab calls `GET /api/secret-stashes/{id}/assignments` (likely needs a new endpoint)
- Upravit uses existing `PUT /api/secret-stashes/{id}`
- Poklady tab uses `SecretStashDetailDto.TreasureQuests[]` (verify DTO carries them)

NOTES
- MTG tile styling is already in `ovcinahra-theme.css` as `.oh-lc-tile-*` — copy/adapt into `.oh-ss-card-*` for the blown-up version, or extract common `.oh-mtg-*` base classes for reuse.
- Do NOT edit the printed card text anywhere — if the DxMemo label says "Popis na tištěné kartě" or similar, it's metadata for the organizer's record, not the card itself.
