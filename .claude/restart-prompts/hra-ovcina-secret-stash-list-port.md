# Handoff — port the SecretStash gallery (2026-04-23)

Load the `hra-ovcina-tinkerer` skill first. Then read this file and `docs/design/dialogs/secret-stash-list.md` (the designer brief) before touching code.

## What this is

Claude Design shipped the mockup for `/secret-stashes` — a gallery of 5:7 MTG tiles replacing the current DxGrid. Your job is to port it to production Blazor on a new branch and ship it via PR.

- **Bundled mockup:** `C:\Users\TomášPajonk\Downloads\Tajn_ skr_e - Gallery _standalone_.html` (1.3 MB, gitignored when copied into the repo).
- **Decoded HTML:** copy the bundled file into `docs/design/dialogs/secret-stash-list.mockup.html` (already gitignored) and decode the `__bundler/template` JSON script to get raw HTML:
  ```python
  import re, json
  with open("docs/design/dialogs/secret-stash-list.mockup.html", encoding="utf-8") as f: h = f.read()
  with open("docs/design/dialogs/secret-stash-list.mockup.unbundled.html", "w", encoding="utf-8") as o:
      o.write(json.loads(re.search(r'<script type="__bundler/template"[^>]*>(.*?)</script>', h, re.S).group(1).strip()))
  ```

Designer notes live in h4 sections at the bottom of the decoded file — read *Proč MTG-dlaždice?*, *Režimy & chipy*, *Interakce* before making structural choices.

## Decisions the designer locked

Confirmed from the mockup + h4 notes.

| Area | Choice |
|---|---|
| Layout | Gallery of 5:7 MTG tiles, **not** a DxGrid. |
| Tile density | **6–8 columns** on wide desktop (`repeat(8, minmax(0, 120px))`), 6 at midsize (`repeat(6, minmax(0, 110px))`), 3 at tablet, 2 at mobile. Denser than the original 4-col brief draft — keep the designer's values. |
| Tile aspect | Exactly 5:7 (2.5 × 3.5 in MTG). |
| Tile content | Image · Name · TWO micro-chips (count of Poklady + count of Hry/Lokace). No description. |
| Mode toggle | Segmented control "Pro hru" / "Katalog" in the toolbar. |
| Filters | Toggles: *jen s poklady* · *jen nepřiřazené* (catalog) · *jen bez lokace*. |
| Catalog overlay | Per-tile "Přiřadit ke hře" pill (forest-green) for unassigned · "V této hře" muted pill for assigned. |
| Click behavior | Tile → direct navigation to `/secret-stashes/{id}`. No quick-peek popup — the tile IS the peek. |
| Empty state | Drozd 120×120 + "Žádné skrýše přiřazeny k této hře." + [Otevřít katalog] secondary + [+ Nová skrýš] primary. |
| New-stash flow | Mini-popup with Název + Popis + ImagePicker; footer CTA = **"Uložit a otevřít"** → POST + navigate to the new stash's detail page. |

## Starting state

### Branches
- `main` currently at v0.12.1. The earlier character-kingdom + location-list + location-detail work is already merged.
- **Your new branch:** `feat/secret-stash-list-port` off latest `main`.

### Files already in place
- `docs/design/dialogs/secret-stash-list.md` — designer brief
- `docs/design/dialogs/secret-stash-detail.md` — brief for the detail page (the click target; may not be ported yet — check)
- `src/OvcinaHra.Client/Components/LocationStashTile.razor` — starting point for the tile. Either extend with a `Size` parameter, or build a new `SecretStashGridTile` component (recommended; brief mentions extracting `.oh-mtg-tile-*` common rules if the duplication hurts — do it if it helps, otherwise skip).

### Class prefixes in the mockup
The decoded file uses flat class names (`ss-*`, `chip-*`, `glyph-*`, `treasures`, `locations`, `state`, `tb-*`). When porting, **prefix everything with `.oh-ssg-*`** (secret-stash-grid) to avoid colliding with the existing `.oh-lc-*` detail page and `.oh-lc-tile-*` Skrýše-tab styling.

## Target files (this branch)

- **Rewrite** `src/OvcinaHra.Client/Pages/SecretStashes/SecretStashList.razor` — drop DxGrid; render a CSS grid of tile links; keep dual-mode routing (`?catalog=true`).
- **New** `src/OvcinaHra.Client/Components/SecretStashGridTile.razor` — tile component (different sizing from `LocationStashTile` — 120 px wide here vs 70 px in the grid's inline Skrýše cell).
- **New** `src/OvcinaHra.Client/Components/NewSecretStashPopup.razor` — mini-popup: Název · Popis · ImagePicker (`EntityType="secretstashes"`, `AspectRatio="5:7"`, `Width="200px"`). Footer `[Zrušit]  [Uložit a otevřít]` → POSTs then navigates to `/secret-stashes/{newId}`.
- **Append** `src/OvcinaHra.Client/wwwroot/css/ovcinahra-theme.css` — `.oh-ssg-*` block.

## Acceptance criteria

- [ ] `dotnet build OvcinaHra.slnx` clean, zero new warnings (`TreatWarningsAsErrors=true`)
- [ ] `dotnet test` green (no API changes expected; regression check only)
- [ ] `/secret-stashes` renders a grid of MTG tiles at 5:7 (check with devtools — aspect-ratio must hold at every breakpoint)
- [ ] Tile count per row: 6–8 desktop, 6 medium, 3 tablet, 2 mobile
- [ ] Each tile shows name + two micro-chips (count of treasures + count of games-or-locations)
- [ ] Click → navigates to `/secret-stashes/{id}`
- [ ] `?catalog=true` toggles show the overlay "Přiřadit ke hře" pills on unassigned tiles
- [ ] "+ Nová skrýš" opens the mini-popup; "Uložit a otevřít" POSTs and navigates to the new detail page
- [ ] Empty state shows Drozd + CTAs (Drozd art: still placeholder — see `feedback_midjourney_creature_style` memory for tool guidance, Recraft.ai is the current recommendation for the real asset)
- [ ] DevTools console clean — no DxGrid attribute errors (MEMORY `feedback_ovcinahra_devexpress_runtime_attrs`)
- [ ] Czech diacritics render in toolbar + tiles + popup

## Testing expectations

- **Integration tests:** no new endpoints — run `dotnet test` to confirm nothing regressed. The existing stash CRUD endpoints cover the POST/PUT/DELETE.
- **Playwright E2E:** add a test that navigates to `/secret-stashes`, clicks a tile, verifies the URL lands on `/secret-stashes/{id}`. Reference existing tests in `tests/OvcinaHra.E2E/`.

## PR workflow

```
git checkout -b feat/secret-stash-list-port main
# ... implement ...
dotnet build OvcinaHra.slnx
dotnet test
git add -p && git commit   # [v0.12.x] tag in commit msg per skill
git push -u origin feat/secret-stash-list-port
gh pr create --base main --head feat/secret-stash-list-port \
  --title "feat(secret-stashes): MTG gallery replaces grid + mini-popup new flow [v0.12.x]" \
  --body "See docs/design/dialogs/secret-stash-list.md and this file for design intent. Drops DxGrid; gallery is CSS-only. New component: SecretStashGridTile + NewSecretStashPopup."
```

Wait for CI + Copilot review. Fixup commit if needed. Merge with `gh pr merge <N> --squash --delete-branch`. Verify deploy via `curl https://api.hra.ovcina.cz/api/version` after ~8 min.

## Gotchas

- **`TreatWarningsAsErrors=true`** — watch out for unused variables in tile components; Razor pattern-match `@if (x is int y)` warns when `y` is unused.
- **Czech pluralization:** "N Poklady" is wrong. Use 0/1/2-4/5+ rule: "žádný poklad" / "1 poklad" / "2 poklady" / "5 pokladů". Reuse a helper if the project has one, or crib from `LocationStashTile.PluralForm`.
- **Tile aspect ratio** must be 5:7 **exactly** — `aspect-ratio: 5/7; object-fit: cover`. Never skew or crop non-uniformly.
- **ImagePicker `EntityType` is lowercase plural** — `secretstashes` (not `SecretStash`). MEMORY note for the Character port caught the same bug.
- **Route English, UI Czech** — the route is `/secret-stashes`, not `/tajne-skryse`. Page title and all chrome in Czech.
- **No description on tiles** — the brief is firm on this. The detail page shows the description; the tile only shows name + count chips.
- **Mockup mini-popup CTA is "Uložit a otevřít"** — not "Uložit". Matters because it changes the flow (POST + navigate vs just POST).

## Follow-ups

1. **Extract `.oh-mtg-tile-*` common base** — if duplication between `LocationStashTile` and `SecretStashGridTile` becomes obvious. Not required.
2. **Playwright E2E** for the gallery.
3. **SecretStash detail page** — the tile click target. Brief is at `docs/design/dialogs/secret-stash-detail.md`; may not be ported yet. If the tile click would currently 404, flag it in your PR so reviewers know.

## Companion references

- Brief: `docs/design/dialogs/secret-stash-list.md`
- Sibling detail brief: `docs/design/dialogs/secret-stash-detail.md`
- Design system onboarding: `docs/design/CLAUDE-DESIGN-SETUP.md`
- Builder template: `docs/design/BUILDER-BRIEF-TEMPLATE.md`
- MJ / Drozd art pipeline: `feedback_midjourney_creature_style` in Azra's MEMORY
