# Handoff — port the Monster bestiary list (2026-04-23)

Load the `hra-ovcina-tinkerer` skill first. Then read this file and `docs/design/dialogs/monster-list.md` (the designer brief) before touching code.

## What this is

Claude Design shipped the mockup for `/monsters` — a card-row bestiary replacing the current 13-col DxGrid. Your job is to port it to production Blazor on a new branch and ship it via PR.

- **Bundled mockup:** `C:\Users\TomášPajonk\Downloads\P_ery - Bestiary List _standalone_.html` (1.37 MB, gitignored when copied into the repo).
- **Decoded HTML:** copy the bundled file into `docs/design/dialogs/monster-list.mockup.html` (gitignored) and decode the `__bundler/template` JSON script with:
  ```python
  import re, json
  with open("docs/design/dialogs/monster-list.mockup.html", encoding="utf-8") as f: h = f.read()
  with open("docs/design/dialogs/monster-list.mockup.unbundled.html", "w", encoding="utf-8") as o:
      o.write(json.loads(re.search(r'<script type="__bundler/template"[^>]*>(.*?)</script>', h, re.S).group(1).strip()))
  ```
- Designer notes live in h4 sections at the bottom of the decoded file — read *Proč card-rows a ne DxGrid?*, *Barvy & chipy*, *Interakce* before making structural choices.

## Decisions the designer locked

Confirmed from mockup markup + h4 notes.

| Area | Choice |
|---|---|
| Layout | **Card-row list**, not a DxGrid. Each row is a horizontal card with thumb + name + chips + stats + hover actions. |
| Row grid template | `grid-template-columns: 96px 1fr auto auto` (per-hra) / `96px 1fr auto auto auto` (catalog has extra column for assign pill). |
| Row height | 96 px. |
| Thumbnail | 96 × 72 px, **aspect-ratio 4:3**, rounded corners, left edge of row. |
| Stat pills | 5 inline mono pills per row: Útok · Obrana · Životy · XP · Groše. Class pattern: `.mon-pill` with `.lbl` (label) + `.val` (mono value). Groše pill uses the gold accent treatment (class `gold`). |
| MonsterType chip | Compact `.mtype` chip on the row, inherits the neutral palette (no per-type color mapping — keep it muted). |
| Kingdom/category badge | Small `.kbadge` beside the name for Kategorie rank (NOT the kingdom palette — reuses the class name but is advisory only). |
| Hover icons | Right-edge hover-reveal group (`.hi-*` classes): Upravit · Přidat kořist · Zkoušet (→ /monsters/{id} Bojovka tab) · Smazat (ghost-bordeaux). Opacity 0 → 0.8 on row hover. |
| Quick-peek popup | DxPopup Karta view: 400 px illustration left, stats block right (`grid-template-columns: 400px 1fr`), footer `[Zavřít]  [Otevřít detail →]` (navigates to /monsters/{id}). |
| Filters | Druh · Kategorie · Tagy multi-select · toggle *jen s kořistí* · toggle *jen nepřiřazené* (catalog only). |
| Mode toggle | Segmented control [Pro hru] / [Katalog] in title strip. |
| Empty state | Drozd 120×120 + "Žádné příšery přiřazeny k této hře." + [Otevřít katalog] secondary + [+ Nová příšera] primary. |

## Class prefixes the mockup uses

Flat names in the mockup. **Prefix everything with `.oh-mon-*`** when porting to avoid collision with existing `.oh-lc-*` and `.oh-ssg-*` blocks:

- `mon-*` (row shell, name, chips) → `.oh-mon-*`
- `pill` / `lbl` / `val` → `.oh-mon-pill`, `.oh-mon-pill-lbl`, `.oh-mon-pill-val`
- `hi-*` (hover icons) → `.oh-mon-hi-*`
- `mtype` → `.oh-mon-mtype`
- `kbadge` → `.oh-mon-kbadge` (keep the `k` prefix locally — it's advisory only, NOT the kingdom palette)
- `gold` → `.oh-mon-gold` (the Groše pill accent)
- `karta-*` → `.oh-mon-karta-*` (popup's Karta view)
- `state-*` → `.oh-mon-state-*` (empty state)
- `assign-*` → `.oh-mon-assign-*` (catalog assign pill)
- `danger-*` → `.oh-mon-danger-*` or reuse `.oh-btn-ghost-bordeaux` from existing theme

## Starting state

### Branches
- `main` currently at v0.12.1 with the location redesign merged. Verify before branching.
- **Your new branch:** `feat/monster-list-port` off latest `main`.

### Files already in place
- `docs/design/dialogs/monster-list.md` — designer brief
- `docs/design/dialogs/monster-detail.md` — sibling brief for the detail page (the "Otevřít detail →" target; may not be ported yet)
- `src/OvcinaHra.Client/Pages/Monsters/MonsterList.razor` — existing DxGrid version to replace
- `src/OvcinaHra.Client/Components/LocationStashTile.razor` — reference for the 5:7 MTG tile pattern (not directly reused here, but shows our tile component pattern)

## Target files (this branch)

- **Rewrite** `src/OvcinaHra.Client/Pages/Monsters/MonsterList.razor` — drop DxGrid, render a scrollable div of card rows; keep dual-mode routing (`?catalog=true`).
- **New** `src/OvcinaHra.Client/Components/MonsterRow.razor` — card-row component. Takes a `MonsterListDto`, renders thumb + name + chips + 5 stat pills + hover actions.
- **Append** `src/OvcinaHra.Client/wwwroot/css/ovcinahra-theme.css` — `.oh-mon-*` block.
- **Modify** `LocationList.razor`-style quick-peek popup reuse? No — monster card has different layout (4:3 illustration, 5 big stats). Build a dedicated `MonsterQuickPeek.razor` inside the page or as a small component.

## Acceptance criteria

- [ ] `dotnet build OvcinaHra.slnx` clean, zero new warnings (`TreatWarningsAsErrors=true`)
- [ ] `dotnet test` green (no API changes; regression only)
- [ ] `/monsters` renders as a vertical list of 96 px card rows
- [ ] Each row has: 96×72 4:3 thumb · Name (Merriweather 700) · MonsterType chip · Kategorie badge · up to 3 tag chips · 5 stat pills (ÚT/OBR/ŽIV/XP/Groše, mono) · hover-reveal right-edge icons
- [ ] `?catalog=true` adds a per-row assign state (muted "✓ v této hře" or hover-reveal "+ Přiřadit ke hře" pill)
- [ ] Row click (outside hover icons) opens the quick-peek DxPopup; popup's "Otevřít detail →" navigates to `/monsters/{id}`
- [ ] Filters Druh · Kategorie · Tagy · jen s kořistí · jen nepřiřazené all functional
- [ ] Empty state shows Drozd + CTAs
- [ ] DevTools console clean — no DxGrid attribute errors (MEMORY `feedback_ovcinahra_devexpress_runtime_attrs`)
- [ ] Czech diacritics render in toolbar + filters + row labels + popup

## Testing expectations

- **Integration tests:** no new endpoints — run `dotnet test` to confirm nothing regressed.
- **Playwright E2E:** add a test that navigates to `/monsters`, filters by Druh, clicks a row, verifies quick-peek popup opens, clicks "Otevřít detail →", URL lands on `/monsters/{id}`. Reference existing tests in `tests/OvcinaHra.E2E/`.

## PR workflow

```
git checkout -b feat/monster-list-port main
# ... implement ...
dotnet build OvcinaHra.slnx
dotnet test
git add -p && git commit   # [v0.12.x] tag in commit msg per skill
git push -u origin feat/monster-list-port
gh pr create --base main --head feat/monster-list-port \
  --title "feat(monsters): card-row bestiary replaces DxGrid + quick-peek popup [v0.12.x]" \
  --body "See docs/design/dialogs/monster-list.md and docs/port-handoffs/hra-ovcina-monster-list-port.md for design intent. Drops 13-col DxGrid. New component: MonsterRow. Preserves dual-mode routing."
```

Wait for CI + Copilot review. Fixup commit if needed. Merge with `gh pr merge <N> --squash --delete-branch`. Verify deploy via `curl https://api.hra.ovcina.cz/api/version` after ~8 min.

## Gotchas

- **`TreatWarningsAsErrors=true`** — watch out for unused variables in the MonsterRow component; Razor pattern-match `@if (x is int y)` warns when `y` is unused.
- **Card-row is not DxGrid** — dropping DxGrid means losing its LayoutKey persistence, column-chooser, and built-in filter UI. The brief accepts this; if you want filter/sort/grouping to persist, implement localStorage manually. Call it out in the PR body.
- **MonsterListDto.TagsDisplay** is `string.Join(", ", TagNames)` — use it as a scratch string for the tag chip cluster rendering, but render per-tag chips individually (don't show the comma-joined string verbatim).
- **MonsterType has its own palette on the mockup chips** — but per the brief and `feedback_ovcina_kingdom_seal_palette`, DO NOT use kingdom colors here. The mockup's `.mtype` chip is neutral forest-green / parchment — keep it that way.
- **Stats are integers on the flat DTO**, not the CombatStats value object (the ListDto flattens them: `Attack`, `Defense`, `Health`). Pull directly.
- **Groše pill (`.oh-mon-gold`)** is subtly amber-tinted to distinguish currency from skill stats — keep the accent, don't use the kingdom Arnor color.
- **Hover actions (`.oh-mon-hi-*`)** should use `bi-play-fill` for Zkoušet → `/monsters/{id}?tab=bojovka` (the detail page's Bojovka tab). The query-string isn't implemented yet on detail page — either add it (routes the detail page to Bojovka on load) or open plain `/monsters/{id}` and let the user switch tabs.
- **Kořist icon (`bi-plus-square`)** is the "add loot" shortcut — on click it can open a tiny flyout picker or just navigate to the detail's Kořist tab. Start with navigate-to-tab if that's simpler.

## Follow-ups

1. **Playwright E2E** — navigation + filter + quick-peek + detail-jump flow.
2. **Monster detail page** — the "Otevřít detail →" target. Brief at `docs/design/dialogs/monster-detail.md`; no mockup or port yet. If the detail page doesn't exist when this lands, the Otevřít detail button will 404 — **flag this prominently in the PR** so reviewers know.
3. **Consider extracting stat-pill styling** (`.oh-mon-pill-*`) as a generic `.oh-stat-pill-*` for reuse when MonsterDetail and Quests land (they'll want similar inline stat displays).

## Companion references

- Brief: `docs/design/dialogs/monster-list.md`
- Sibling detail brief: `docs/design/dialogs/monster-detail.md`
- Design system onboarding: `docs/design/CLAUDE-DESIGN-SETUP.md`
- Builder template: `docs/design/BUILDER-BRIEF-TEMPLATE.md`
- Prior port for reference: `docs/port-handoffs/hra-ovcina-secret-stash-list-port.md`
