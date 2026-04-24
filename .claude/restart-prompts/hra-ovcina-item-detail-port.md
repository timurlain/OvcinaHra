# Handoff — port the Item detail page (2026-04-23)

Load the `hra-ovcina-tinkerer` skill first. Then read this file and `docs/design/dialogs/item-detail.md` (the designer brief) before touching code.

## What this is

Claude Design shipped the mockup for `/items/{id:int}` — an illuminated MTG-card page with 5 tabs (Karta · Upravit · Tvorba · Výskyt · Obchod). Your job is to port it to production Blazor on a new branch and ship it via PR.

- **Bundled mockup:** `C:\Users\TomášPajonk\Downloads\P_edm_t - Detail _standalone_.html` (1.53 MB, gitignored when copied in).
- **Decoded HTML:** copy the bundled file into `docs/design/dialogs/item-detail.mockup.html` (already gitignored) and decode via:
  ```python
  import re, json
  with open("docs/design/dialogs/item-detail.mockup.html", encoding="utf-8") as f: h = f.read()
  with open("docs/design/dialogs/item-detail.mockup.unbundled.html", "w", encoding="utf-8") as o:
      o.write(json.loads(re.search(r'<script type="__bundler/template"[^>]*>(.*?)</script>', h, re.S).group(1).strip()))
  ```
- Designer notes at the bottom of the decoded file (h4 sections): *Sdílené komponenty*, *Dodržené anti-patterny*, *Open questions*.

## Two important pivots from the original brief

1. **Hero is 5:7 MTG card, NOT 4:3 photo.** The brief said 4:3 (photo-style) — designer picked 5:7 because items in the LARP are *literally* printed as MTG-sized cards. The detail page now renders the item the same way the card is held at the table. Only `aspect-ratio: 5/7` appears anywhere in the mockup CSS.
2. **Tab is "Tvorba" not "Crafting"** (Czech, matches the rest of the nav). Count badges are rendered in the tab strip: Tvorba (N), Výskyt (N), Obchod (N).

Trust the mockup over the brief where they disagree.

## Decisions the designer locked

Confirmed from the mockup markup, CSS, and h4 notes.

| Area | Choice |
|---|---|
| Tabs | **5, in order:** Karta · Upravit · Tvorba · Výskyt · Obchod. Default = Karta. Tab strip sticky under the title strip. Count badges on Tvorba/Výskyt/Obchod (no count on Karta/Upravit). |
| Hero | **5:7 MTG card** (aspect-ratio: 5/7, object-fit: cover). Parchment frame. |
| Karta layout | 2-col `60fr 40fr` (or `62fr 38fr` for the empty-state variant): LEFT = 5:7 hero · RIGHT = requirements + flags. Below: h3 *Efekt* · h3 *Fyzická podoba* (note: Podoba is its own section on Karta, not a chip). |
| Stat rows | Icon + LABEL + value pattern (`grid-template-columns: 22px 1fr auto`). Class pip icons + reqWarrior/Archer/Mage/Thief numbers, unique/limited/craftable boolean rows. |
| Upravit sections | **5 sections** (not 6 as brief had): Identita · Vizuál · Vlastnosti · Požadavky · Obsah. DxFormLayout 2-col. Sticky footer (Smazat ghost-bordeaux far-left when editing · Zrušit · Uložit). |
| Tvorba tab | Two-panel recipe graph: "Vyrábí se z (N receptů)" + "Používá se v (N receptech)". Per-game recipe cards with ingredient chain → this item. Location of crafting + required building + required skill shown as muted chips. |
| Výskyt tab | Three sub-sections: "Kořist u příšer (N)" · "Odměny v questech (N)" · "V pokladech (N)". Each section is a mini-card grid (`repeat(auto-fill, minmax(260px, 1fr))`). |
| Obchod tab | Table-like list of GameItem rows ("V obchodě (N hry)"). Per-game Cena / Sklad / Prodává / Nalezitelný / Podmínka / Akce. Each row has "GameId: N · upravit" inline edit link. |
| Empty state (Karta) | New item shows Drozd perch in the 5:7 slot + "Nahrát fotku" CTA. |

## Aspect ratios in CSS

**Only `5/7` appears.** All item visuals — hero on Karta, ingredient tiles on Tvorba, reward mini-cards on Výskyt — share the MTG proportion. Makes the page feel like a card-reference sheet.

## Grid templates in CSS (distinct)

```
60fr 40fr                                 — Karta columns (hero vs sidebar)
62fr 38fr                                 — empty-state Karta variant
22px 1fr auto                             — stat row (icon + label + value)
48px 1fr auto auto                        — Tvorba recipe row (ingredient thumb + name + qty)
72px 1fr auto                             — compact Obchod/Výskyt row
repeat(auto-fill, minmax(260px, 1fr))     — Výskyt sub-section mini-card grids
repeat(3,1fr) / repeat(4,1fr)             — class pip / requirement rows
32px 1fr 32px                             — centered section with side gutters
1fr auto auto auto                        — flexible left + 3 right-pinned actions
```

## Class prefixes in the mockup

Flat names: `stat-*`, `slab`, `sval`, `sic` (stat label/value/icon), `sect-*`, `ichip` (ItemType chip), `req-*`, `lv`, `lvnum` (level numerals), `mini-*` (mini cards), `iname`, `glyph`. **When porting, prefix under `.oh-it-d-*`** (item-detail) to coexist with `.oh-it-*` from the item-list port and `.oh-mon-d-*` from the monster-detail port.

- `stat-slab/sval/sic` → `.oh-it-d-stat-lbl`, `.oh-it-d-stat-val`, `.oh-it-d-stat-icon`
- `ichip` → `.oh-it-d-ichip`
- `req-*` + `lv/lvnum` → extract the **shared** `ItemClassPips` component (already flagged in the item-list handoff — re-use here)
- `mini-*` → `.oh-it-d-mini-*` for Výskyt sub-section mini cards

## Starting state

### Branches
- `main` at v0.12.1
- **Your new branch:** `feat/item-detail-port` off latest `main`

### Files already in place
- `docs/design/dialogs/item-detail.md` — designer brief
- Sibling `docs/design/dialogs/item-list.md` — list brief (may be ported as `feat/item-list-port` in flight)
- `src/OvcinaHra.Client/Pages/Items/ItemList.razor` — existing; row click currently opens a popup. After the list-port lands, row click will open a quick-peek popup with "Otevřít detail →" targeting `/items/{id}` — your page fills that target.
- `src/OvcinaHra.Client/Components/LocationStashTile.razor` — the 5:7 MTG tile reference

### Files to touch (this branch)

- **New page** `src/OvcinaHra.Client/Pages/Items/ItemDetail.razor` — route `/items/{id:int}`
- **New or shared component** `src/OvcinaHra.Client/Components/ItemClassPips.razor` — if the list-port already shipped it, reuse. If not, create here and the list-port picks it up when it lands.
- **New component** `src/OvcinaHra.Client/Components/CraftingRecipeCard.razor` — ingredient chain → output display, with location/building/skill chips. Will also serve the future CraftingRecipe detail page.
- **New component** `src/OvcinaHra.Client/Components/MonsterLootMiniCard.razor` — tiny 5:7-proportioned card for the Výskyt › Kořist sub-section (monster thumb + name + × qty).
- **Append** `.oh-it-d-*` block to `src/OvcinaHra.Client/wwwroot/css/ovcinahra-theme.css`.

## Acceptance criteria

- [ ] `dotnet build OvcinaHra.slnx` clean, zero new warnings (`TreatWarningsAsErrors=true`)
- [ ] `dotnet test` green
- [ ] `/items/{id}` renders with the 5-tab strip sticky under the title; tab count badges present on Tvorba / Výskyt / Obchod
- [ ] Karta: 2-col `60fr 40fr`; hero at 5:7; Efekt + Fyzická podoba sections below; class-pip values light up per DTO reqs
- [ ] Upravit: 5 DxFormLayout sections (Identita · Vizuál · Vlastnosti · Požadavky · Obsah); sticky footer
- [ ] Tvorba: two panels. Panel A lists recipes that produce this item (per-game cards with ingredient chain + required building/skill/location chips). Panel B lists recipes consuming this item (compact rows with × qty + game badge).
- [ ] Výskyt: 3 sub-section mini-card grids (`repeat(auto-fill, minmax(260px, 1fr))`) — Kořist u příšer · Odměny v questech · V pokladech. Empty-state message per sub-section.
- [ ] Obchod: table rows for GameItem per game (Cena mono with coin accent · Sklad · Prodává / Nalezitelný badges · Podmínka italic · Akce icons)
- [ ] Empty-state Karta (no photo, no effect) shows Drozd + "Nahrát fotku" CTA
- [ ] DevTools console clean; no DxGrid attribute errors (no DxGrid on this page, but verify)
- [ ] Czech diacritics render throughout

## Testing expectations

- **Integration tests:** verify crafting endpoints exist:
  - `GET /api/crafting/recipes?outputItemId={id}` (Panel A)
  - `GET /api/crafting/recipes?ingredientItemId={id}` (Panel B)
  If either is missing, add it and cover with a Testcontainers test.
- **Playwright E2E:** navigate `/items` → click row → quick-peek → "Otevřít detail →" → verify Karta loads at 5:7, click Tvorba tab, verify recipe card renders.

## PR workflow

```
git checkout -b feat/item-detail-port main
# ... implement ...
dotnet build OvcinaHra.slnx
dotnet test
git add -p && git commit   # [v0.12.x] tag in commit msg per skill
git push -u origin feat/item-detail-port
gh pr create --base main --head feat/item-detail-port \
  --title "feat(items): /items/{id} detail page with 5 tabs (MTG hero, crafting graph, výskyt, obchod) [v0.12.x]" \
  --body "See docs/design/dialogs/item-detail.md and .claude/restart-prompts/hra-ovcina-item-detail-port.md for decisions. Hero is 5:7 MTG (pivot from brief's 4:3 — items are physical cards in the LARP). Tvorba tab renders both sides of the recipe graph. Výskyt aggregates MonsterLoot + QuestReward + TreasureItem. Obchod shows per-game GameItem config with coin-accented price."
```

Squash-merge with `gh pr merge <N> --squash --delete-branch` after CI + Copilot. Verify deploy via `curl https://api.hra.ovcina.cz/api/version`.

## Gotchas

- **Hero is 5:7, NOT 4:3.** Use `aspect-ratio: 5/7; object-fit: cover;`. Never skew. If an item photo is landscape originally, the frame crops to 5:7 — that's by design.
- **`TreatWarningsAsErrors=true`** — pattern-match `@if (x is int y)` warns when `y` unused.
- **`ClassRequirements` is a value object** (already flattened on the DTO — just read `ReqWarrior/Archer/Mage/Thief` directly).
- **Upravit has 5 sections**, not 6. Rebuild the CombatStats-equivalent pattern here: edit flat (4 DxSpinEdit for req classes + 3 DxCheckBox for flags + DxMemo for Effect + DxComboBox for Type/Podoba + DxTextBox for Name), rebuild value objects in the save handler.
- **Podoba (PhysicalForm)** shows as its own **section on Karta** (not just a chip in the subline). Render as h3 + description or muted paragraph.
- **Tvorba endpoints may not exist yet.** `GET /api/crafting/recipes?outputItemId=X` and `...?ingredientItemId=X` are implied by the mockup; the existing `CraftingRecipe` entity supports both queries but the API surface may need extending. Testcontainers integration test before wiring the tab.
- **Výskyt mini-card grids** share the 5:7 proportion everywhere — monster drops, quest rewards, treasure items all render as 5:7 mini-cards. Don't mix aspect ratios. Extract `MonsterLootMiniCard`, `QuestRewardMiniCard`, `TreasureItemMiniCard` OR one generic `MiniLootCard` with a `Kind` enum.
- **Obchod coin accent** — use the same gold tint as the item-list Cena pill. Extract `.oh-coin-pill` if you haven't already.
- **`IsUnique` / `IsLimited` / `IsCraftable`** show as stat rows on Karta AND as DxCheckBox on Upravit. Keep them consistent.
- **Existing current-game hint** — mockup shows `GameId: 14 · upravit` style inline links on recipe cards + Obchod rows. Clicking navigates to that game's page OR opens an inline edit popup — designer's call, your judgment. Keep scope tight: read-only link for now, popup in follow-up.
- **MonsterLoot Quantity** is shown as "× N" (mono pill, burnt-orange outline). Never "x N" lowercase.

## Follow-ups

1. **Playwright E2E** — navigate-click-tab-switch flow.
2. **Shared mini-card component** — if Výskyt sub-sections duplicate styling, consolidate to `MiniLootCard<T>` or similar.
3. **Inline recipe-edit popup** on Tvorba — mockup hints at `"GameId: N · upravit"` being a navigate. If you add an inline popup here, match the item-list new-stash pattern.
4. **Shared `CraftingRecipeCard`** — flagged reusable for a future `CraftingRecipe` detail page. Keep its API generic (inputs · outputs · requirements) so it's portable.
5. **Extract `.oh-coin-pill`** shared base — used on item-list Cena column and ItemDetail Obchod rows.

## Companion references

- Brief: `docs/design/dialogs/item-detail.md`
- Sibling list brief: `docs/design/dialogs/item-list.md`
- List port handoff: `.claude/restart-prompts/hra-ovcina-item-list-port.md`
- Similar-pattern ports: `.claude/restart-prompts/hra-ovcina-monster-detail-port.md`, `.claude/restart-prompts/hra-ovcina-secret-stash-list-port.md`
- MTG tile precedent: `src/OvcinaHra.Client/Components/LocationStashTile.razor`
- Design system onboarding: `docs/design/CLAUDE-DESIGN-SETUP.md`
- Builder template: `docs/design/BUILDER-BRIEF-TEMPLATE.md`
