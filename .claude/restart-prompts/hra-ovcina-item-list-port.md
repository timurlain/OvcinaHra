# Handoff — port the Items grid (2026-04-23)

Load the `hra-ovcina-tinkerer` skill first. Then read this file and `docs/design/dialogs/item-list.md` (the designer brief) before touching code.

## What this is

Claude Design shipped the Items grid mockup. **Important pivot from the brief:** the designer **kept DxGrid** (instead of switching to a photo gallery / card-row hybrid as I originally proposed). The grid gains a photo column, a type-dot column, four class pips, and a 3-cell flags box — but the DxGrid chassis stays, along with its LayoutKey persistence, column chooser, filter menu, and virtual scroll.

Trust the mockup over the brief where they disagree. Rationale: operators scan dozens of items looking for the right drop / reward / shop fit — a dense tabular view beats a gallery for that workflow.

- **Bundled mockup:** `C:\Users\TomášPajonk\Downloads\P_edm_ty - Grid _standalone_.html` (1.37 MB, gitignored when copied into the repo).
- **Decoded HTML:** copy the bundled file into `docs/design/dialogs/item-list.mockup.html` (already gitignored) and decode via:
  ```python
  import re, json
  with open("docs/design/dialogs/item-list.mockup.html", encoding="utf-8") as f: h = f.read()
  with open("docs/design/dialogs/item-list.mockup.unbundled.html", "w", encoding="utf-8") as o:
      o.write(json.loads(re.search(r'<script type="__bundler/template"[^>]*>(.*?)</script>', h, re.S).group(1).strip()))
  ```
- Designer notes live at the bottom of the decoded file (h4 sections): *DxGrid kompatibilita*, *Foto sloupec*, *Typ-dot sloupec*, *Třídy — 4 mono pips*, *Cena + Sklad*, *Flagy — 3-buňkový box*.

## Decisions the designer locked

Confirmed from the mockup CSS + h4 notes.

| Area | Choice |
|---|---|
| Grid framework | **Keep DxGrid.** Do NOT replace with a CSS gallery. Keeps OvcinaGrid wrapper, LayoutKey persistence, column chooser, filter menu. |
| Dual mode | Keep both (`?catalog=true` toggles) |
| LayoutKey | Bump to `grid-layout-items-v3` (MEMORY §L — columns change) |
| Foto column | **78 px wide**, 4:3 thumbnail (`aspect-ratio: 4/3`), object-fit:cover. Hidden by default when user groups by Typ (saves width — see column-template variant below). |
| Typ-dot column | **24 px** — a 10 px colored dot per ItemType. Neutral monster-like palette per type (NOT kingdom colors). Tooltip shows ItemType display name. |
| Name column | `minmax(220px, 1.6fr)` — flexible, 220 min |
| Class pips | **4 mono pips** — Válečník / Lučištník / Mág / Zloděj. Lit (forest-green filled) when Req ≥ 1, dim (#d4c4b0) when Req = 0. Tooltip shows exact level. Each pip in its own column, 64/64/64/88 px widths. |
| Cena | Mono pill with **coin/gold accent** (the `.coin` class in the mockup) |
| Sklad | Mono "N/M" format or "—" when stock not configured |
| Flagy | **3-cell box** (96 px wide): `Unikát` / `Limit` / `Craftable`. Each cell lit or dim. Compact, saves column real estate. |
| Quick-peek popup | **720 × 480** DxPopup. Inner grid: `grid-template-columns: 360px 1fr` — 360 px hero photo left, fields right. Footer `[Zavřít]  [Otevřít detail →]` → `/items/{id}`. |
| Filters | Typ (19 types) · Třída · toggle *jen nalezitelné* · toggle *jen prodejné* · (per-hra only) maybe toggle *jen nakupitelné* |
| Grouping | `Group: Typ` dropdown in the toolbar. When grouped, **Foto column hides** (the grid becomes 12-col instead of 13-col to save width). |

## Grid column templates from the mockup CSS

```
/* default per-hra — 13 columns */
4px 78px 24px minmax(220px, 1.6fr) 130px 150px 64px 64px 64px 88px 92px 132px 96px

/* grouped-by-type variant — 12 columns (photo hidden) */
4px 24px minmax(220px, 1.6fr) 130px 150px 64px 64px 64px 88px 92px 132px 96px
```

Reading left → right:

| # | Width | Purpose |
|---|---|---|
| 1 | `4px` | Row selection stripe (DxGrid row accent) |
| 2 | `78px` | **Foto** (4:3, hidden when grouped by Typ) |
| 3 | `24px` | **Typ-dot** |
| 4 | `minmax(220px, 1.6fr)` | **Název** |
| 5 | `130px` | **Typ chip** (text label) |
| 6 | `150px` | **Podoba / Efekt** (whichever reads best — confirm with brief) |
| 7-10 | `64/64/64/88` | **4 class pips** (Válečník / Lučištník / Mág / Zloděj) |
| 11 | `92px` | **Cena** (coin accent) |
| 12 | `132px` | **Sklad** |
| 13 | `96px` | **Flagy 3-buňkový box** |

## Class prefixes in the mockup

Flat names (`col-*`, `pip-*`, `on/lit/dim`, `tdot`, `coin`, `ichip`, `fchip`, `acts`, `edge`). **When porting, prefix under `.oh-it-*`** to avoid collision with other scoped blocks already in theme (`.oh-lc-*`, `.oh-mon-*`, `.oh-ssg-*`).

- `pip-*` + states `on/lit/dim` → `.oh-it-pip`, `.oh-it-pip--lit`, `.oh-it-pip--dim`
- `tdot` (type color dot) → `.oh-it-tdot`
- `coin` (price pill accent) → `.oh-it-coin`
- `ichip` (ItemType chip text) → `.oh-it-ichip`
- `fchip` (filter chip toggles) → `.oh-it-fchip`
- `acts` (hover-reveal action group) → `.oh-it-acts`

If `.oh-it-pip-*` looks like it could be reused for Monster/Quest/etc. — extract `.oh-class-pips-*` as a shared base (already flagged in the design brief).

## Toolbar composition (mockup)

```
Title strip:
  h1 "Předměty"  ·  [Pro hru] / [Katalog] segmented toggle
  right:  [+ Nový předmět] primary

Filter row:
  [ Hledat… ]  [ Typ ▼ 19 items ]  [ Třída ▼ ]
  [ Group: Typ ▼ ]
  [ toggle jen nalezitelné ]  [ toggle jen prodejné ]
  right-aligned:  [ N předmětů ] count badge
```

## Empty state

Drozd 120 × 120 + "Zatím tu nic není…" + `[Otevřít katalog]` secondary + `[+ Nový předmět]` primary.

## Starting state

### Branches
- `main` at v0.12.1
- **Your new branch:** `feat/item-list-port` off latest `main`

### Files to touch

- **Rewrite** `src/OvcinaHra.Client/Pages/Items/ItemList.razor` — add the new columns, bump LayoutKey, wire quick-peek popup, apply filter/group toolbar
- **New component** `src/OvcinaHra.Client/Components/ItemClassPips.razor` — 4-pip row (reusable for Item tile/detail and later Quest rewards). The brief strongly recommends extracting this day 1.
- **New component** `src/OvcinaHra.Client/Components/ItemFlagsBox.razor` — the 3-cell Unikát/Limit/Craftable compact box
- **Append** `src/OvcinaHra.Client/wwwroot/css/ovcinahra-theme.css` — `.oh-it-*` scoped block
- **Modify** `src/OvcinaHra.Client/Pages/Items/ItemList.razor` row click → open quick-peek popup (instead of the current full edit popup). Popup "Otevřít detail →" navigates to `/items/{id}` (NOTE: detail page not shipped yet — flag the 404 risk in your PR)

## Acceptance criteria

- [ ] `dotnet build OvcinaHra.slnx` clean, zero new warnings (`TreatWarningsAsErrors=true`)
- [ ] `dotnet test` green (no API changes — `ItemListDto` already carries `ImageUrl` and the flat class requirement ints)
- [ ] `/items` renders the 13-col DxGrid with Foto · Typ-dot · Název · Typ · Podoba · 4 class pips · Cena · Sklad · Flagy
- [ ] `/items?catalog=true` shows the catalog variant (similar columns, sale/stock columns hidden since catalog has no GameItem data)
- [ ] LayoutKey bumped: `grid-layout-items-v3` — verify by clearing localStorage then reloading
- [ ] Class pips light up correctly based on `ReqWarrior/ReqArcher/ReqMage/ReqThief`
- [ ] Flags 3-cell box reads Unikát / Limit / Craftable flags
- [ ] Cena uses the coin gold accent
- [ ] Typ-dot column shows a colored dot per ItemType (neutral, no kingdom colors)
- [ ] Group by Typ works; when grouped, Foto column is hidden (dynamic visibility switch)
- [ ] Filters Typ · Třída · jen nalezitelné · jen prodejné function
- [ ] Row click opens the 720 × 480 quick-peek popup (hero photo 360 px left, fields right); "Otevřít detail →" attempts navigation (flag 404 until `/items/{id}` ships)
- [ ] DevTools console clean (no DxGrid attribute errors — MEMORY `feedback_ovcinahra_devexpress_runtime_attrs`)
- [ ] Czech diacritics render throughout

## Testing expectations

- **Integration tests:** no new endpoints — run `dotnet test` to confirm nothing regressed.
- **Playwright E2E:** navigate `/items`, filter by Typ = Zbraň, verify rows filter, click a row, popup opens, close popup, click another row, popup opens again with new data. Reference existing tests in `tests/OvcinaHra.E2E/`.

## PR workflow

```
git checkout -b feat/item-list-port main
# ... implement ...
dotnet build OvcinaHra.slnx
dotnet test
git add -p && git commit   # [v0.12.x] tag in commit msg per skill
git push -u origin feat/item-list-port
gh pr create --base main --head feat/item-list-port \
  --title "feat(items): redesigned /items grid with photo, type-dot, class pips, flags box [v0.12.x]" \
  --body "Implements the Items grid from the Claude Design mockup. Keeps DxGrid chassis (designer pivot from my original brief — see .claude/restart-prompts/hra-ovcina-item-list-port.md). Adds Foto 4:3 column (hidden when grouping by Typ), Typ-dot, 4 class pips (Válečník/Lučištník/Mág/Zloděj), Cena coin accent, Sklad, 3-cell Flagy box. Quick-peek popup 720 × 480 replaces the current edit popup on row click; 'Otevřít detail →' will 404 until /items/{id} ships in a follow-up."
```

Squash-merge with `gh pr merge <N> --squash --delete-branch` after CI + Copilot.

## Gotchas

- **`TreatWarningsAsErrors=true`** — watch out in Razor pattern-match `@if (x is int y)` warns when `y` unused. Use `.HasValue` or `is not null`.
- **Keep DxGrid** — resist the urge to rewrite as a CSS grid. The designer explicitly preserved DxGrid for LayoutKey persistence + column chooser + virtual scroll.
- **LayoutKey bump is mandatory** (MEMORY §L). `grid-layout-items-catalog-v2` → `grid-layout-items-v3` (rename AND bump so stale per-user filters don't replay against the new schema).
- **Group-by-Typ hides Foto column** — implement via `DxGridDataColumn Visible="@(!isGroupedByType)"`. The grouping state comes from `DxGrid.GroupedColumns` inspection or you track it yourself when the user toggles grouping.
- **Photo-column thumbnail** — `ItemListDto.ImageUrl` is already a SAS URL. Don't re-fetch from `/api/images/items/{id}` — it's expensive per-row. Render directly.
- **ItemType dot color** — keep neutral palette. The designer's 19 types are NOT colored like kingdoms. Pick a gentle palette (e.g. weapons sage green · potions amethyst · jewelry burnt gold · armor slate) or stay monotone forest-green for all and use the chip text + icon to disambiguate. Confirm in your port PR.
- **"Otevřít detail →"** will 404 until `/items/{id}` ships (separate port). Flag prominently in the PR body.
- **`ClassRequirements` is a value object** (already flattened on the DTO — just read `ReqWarrior/Archer/Mage/Thief` directly).
- **Flags box** — keep the mockup's 3-cell layout even when only one flag is set. Don't collapse/hide cells (the grid layout depends on consistent column widths).
- **Cena coin accent** — gold `rgba(249, 168, 37, 0.2)` bg + amber border + mono numeral. NOT the kingdom Arnor color (same hex by coincidence — keep it as "coin" semantic, not "kingdom").

## Follow-ups

1. **Playwright E2E** — filter, group, popup, detail-link-404-awareness test.
2. **Extract `.oh-class-pips-*`** as a shared base (reused on ItemDetail's Karta when the detail page lands, plus Quest reward UI later).
3. **`/items/{id}` detail page** — brief at `docs/design/dialogs/item-detail.md`. When that lands, the "Otevřít detail →" button in this port's popup will work. Coordinate.
4. **Catalog mode assign-to-game** — current `SecretStashGrid` port introduced the pattern with per-tile "+ Přiřadit ke hře" overlay. Consider adding similar UX to Items catalog mode (toggle "jen nepřiřazené" filter already part of the filter row above). Separate PR.

## Companion references

- Brief: `docs/design/dialogs/item-list.md`
- Sibling detail brief: `docs/design/dialogs/item-detail.md` (not yet ported)
- Design system onboarding: `docs/design/CLAUDE-DESIGN-SETUP.md`
- Builder template: `docs/design/BUILDER-BRIEF-TEMPLATE.md`
- Prior ports for reference:
  - `.claude/restart-prompts/hra-ovcina-monster-list-port.md` (different choice — dropped DxGrid)
  - `.claude/restart-prompts/hra-ovcina-secret-stash-list-port.md` (different choice — gallery)
  - `.claude/restart-prompts/hra-ovcina-monster-detail-port.md` (sibling page pattern)
