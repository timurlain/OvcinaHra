# Handoff — port the Monster detail page (2026-04-23)

Load the `hra-ovcina-tinkerer` skill first. Then read this file and `docs/design/dialogs/monster-detail.md` (the designer brief) before touching code.

## What this is

Claude Design shipped the mockup for `/monsters/{id:int}` — an illuminated bestiary page with 5 tabs (Karta · Upravit · Bojovka · Kořist · Výskyt). Your job is to port it to production Blazor on a new branch and ship it via PR.

- **Bundled mockup:** `C:\Users\TomášPajonk\Downloads\P_era - Detail _standalone_.html` (1.37 MB, gitignored when copied in).
- **Decoded HTML:** copy the bundled file into `docs/design/dialogs/monster-detail.mockup.html` (already gitignored) and decode via:
  ```python
  import re, json
  with open("docs/design/dialogs/monster-detail.mockup.html", encoding="utf-8") as f: h = f.read()
  with open("docs/design/dialogs/monster-detail.mockup.unbundled.html", "w", encoding="utf-8") as o:
      o.write(json.loads(re.search(r'<script type="__bundler/template"[^>]*>(.*?)</script>', h, re.S).group(1).strip()))
  ```
- Designer notes at the bottom of the decoded file (h4 sections): *Iluminovaná stránka, ne statblok*, *Bojovka = presenter view*, *Kořist a Výskyt*.

## Decisions the designer locked

Confirmed from the mockup markup, CSS, and h4 notes.

| Area | Choice |
|---|---|
| Tabs | **5, in order:** Karta · Upravit · Bojovka · Kořist · Výskyt. Default = Karta. Sticky under the title strip. |
| Tab count badges | Kořist and Výskyt carry a mono count badge (e.g. "Kořist 5", "Výskyt 4"). Karta/Upravit/Bojovka have no count. |
| Karta layout | 2-col `62fr 38fr`: LEFT = illustration 4:3 with parchment frame · RIGHT = vertical stat block with icon + LABEL + value rows. |
| Hero illustration AR | **4:3 landscape** (aspect-ratio: 4/3, object-fit: cover). Parchment frame = 2 px `#c4b494` border + inset gold radial. |
| Stat block icons | bi-lightning-charge Útok · bi-shield-shaded Obrana · bi-heart-pulse Životy · bi-star-fill XP · bi-coin Groše. Mono right-aligned values. Subtle hairline between rows. |
| Under-Karta content | h3 Schopnosti · h3 AI chování · h3 Tagy (chips) · h3 Poznámka organizátorů (the `Notes` field, muted beige callout). |
| Upravit sections | Identita · Vizuál · Bojovka · Odměna · Tagy · Poznámky. 2-col DxFormLayout. Sticky footer (Smazat ghost-bordeaux far-left when editing; Uložit disabled until Name is set). |
| Bojovka tab | Presenter view for live-encounter use on a 10" tablet. Three big stat boxes (`repeat(3,1fr)`) with 3rem mono numerals for Útok/Obrana/Životy. Abilities as numbered bullets, AI chování as Georgia italic pull-quote. Sticky top row: `[−1 Život] [+1 Život] [resetovat]` — **advisory only, local state, no API**. |
| Kořist tab | Per-game grouping. Each game header (h4 "Ovčina 2026 — Stíny Rhovanionu" etc.) followed by a `repeat(auto-fill, minmax(160px, 1fr))` grid of MTG 5:7 item tiles. Each tile shows the item photo, name, and × N quantity pill. "+ Přidat kořist" at end of each row. "+ Přiřadit do další hry" header action. |
| Výskyt tab | Summary row "Přiřazené hry: 4 hry · 11 questů · 23 encounterů". Then a per-game list: Hra · Questy (N) · Encounters (N) · Odebrat. Row click → that game's per-game page. Odebrat confirms via DxPopup (MEMORY §D) with header "Odebrat z hry Ovčina 2023?". |
| HP counter on Bojovka | Purely local `int tempHp` state — no API call, no persistence. "resetovat" button pulls it back to full Health. |
| Title strip subline | `{MonsterType display} · Kategorie {N} · #M{ID:D3}` — e.g. "Humanoid · Kategorie 2 · #M042". |

## Class prefixes in the mockup

Flat class names (`best-*`, `mtype`, `stat/num/val/lab`, `ref`, `sect-*`, `actions`, `state`, `dotsep`, `count`, `tab`, `chip`, `field`). **When porting, prefix all new classes with `.oh-mon-d-*`** (monster-detail) to avoid collision with:
- `.oh-mon-*` (from the monster-list port, if already shipped) — the list page uses `.oh-mon-row-*` and `.oh-mon-pill-*`
- `.oh-lc-*` (location)
- `.oh-ssg-*` (secret-stash grid)

If you find yourself adding stat-pill styling that looks duplicative between MonsterList and MonsterDetail's Bojovka, extract `.oh-stat-pill-*` as a shared rule (brief hints at this).

## Aspect ratios confirmed in CSS

- **4:3** — Karta hero illustration
- **5:7** — Kořist item tiles (MTG)
- **1:1** — probably the tiny round MonsterType/Kategorie chips or tag dots; verify

## Grid templates in CSS

- `62fr 38fr` — Karta columns (illustration vs stats)
- `repeat(3, 1fr)` — Bojovka three big stat boxes
- `360px 1fr` — likely a sub-layout (check context — possibly Upravit's image-slot + form side)
- `repeat(auto-fill, minmax(160px, 1fr))` — Kořist item tile grid
- `22px 1fr auto` — stat row (icon + label + value)

## Starting state

### Branches
- `main` currently at v0.12.1.
- **Your new branch:** `feat/monster-detail-port` off latest `main`.

### Files already in place
- `docs/design/dialogs/monster-detail.md` — designer brief
- `docs/design/dialogs/monster-list.md` — sibling brief; may be ported as `feat/monster-list-port` in flight or merged
- `src/OvcinaHra.Client/Pages/Monsters/MonsterList.razor` — existing list page; row click currently opens an edit popup. On this branch you rewire row click → `NavigationManager.NavigateTo($"/monsters/{id}")` (or keep quick-peek popup + "Otevřít detail →" button, matching LocationList pattern).
- `src/OvcinaHra.Client/Components/LocationStashTile.razor` — reference for the 5:7 MTG tile pattern used by the Kořist tab

## Target files (this branch)

- **New page:** `src/OvcinaHra.Client/Pages/Monsters/MonsterDetail.razor` — route `/monsters/{id:int}`
- **New component:** `src/OvcinaHra.Client/Components/MonsterLootTile.razor` — 5:7 MTG tile showing an Item drop (image + name + × qty pill). Reuses the `.oh-mtg-tile-*` common rules if extracted, otherwise a standalone local CSS block.
- **New component (recommended):** `src/OvcinaHra.Client/Components/TagChipPicker.razor` — multi-select chip picker from the Tag catalog. Will be reused on Quests, Items later. If time-pressed, inline it and extract later.
- **Append** `src/OvcinaHra.Client/wwwroot/css/ovcinahra-theme.css` — `.oh-mon-d-*` scoped block.
- **Modify** `src/OvcinaHra.Client/Pages/Monsters/MonsterList.razor` — row click now navigates to `/monsters/{id}`. If the monster-list-port branch already merged, coordinate — don't re-port the list, just update the navigation path.

## Acceptance criteria

- [ ] `dotnet build OvcinaHra.slnx` clean, zero new warnings (`TreatWarningsAsErrors=true`)
- [ ] `dotnet test` green
- [ ] `/monsters/{id}` renders with the 5-tab strip sticky under a title strip showing `{MonsterType display} · Kategorie {N} · #M{Id:D3}`
- [ ] Karta: 2-col layout, hero at 4:3, stat block right-aligned mono values, Schopnosti/AI/Tagy/Poznámka sections below
- [ ] Upravit: 6 sections (Identita · Vizuál · Bojovka · Odměna · Tagy · Poznámky) in DxFormLayout; sticky footer
- [ ] Bojovka: three big 3rem numerals for ÚT/OBR/ŽIV; local-only HP counter (−1 / +1 / resetovat) that never hits the API
- [ ] Kořist: per-game grouped MTG 5:7 tiles (`auto-fill minmax(160px, 1fr)`); shows the quantity pill; "+ Přidat kořist" per game row
- [ ] Výskyt: summary row + per-game list; Odebrat routed through DxPopup confirm (MEMORY §D)
- [ ] DevTools console clean
- [ ] Czech diacritics render
- [ ] MonsterType chip uses neutral palette (no kingdom colors — MEMORY `feedback_ovcina_kingdom_seal_palette`)

## Testing expectations

- **Integration tests:** verify there's no per-game monster loot aggregate endpoint yet — if not, add `/api/monsters/{id}/loot?gameId=X` returning `List<MonsterLootDto>` grouped/filtered appropriately. Cover with a Testcontainers integration test.
- **Playwright E2E:** navigate `/monsters` → click first row → verify detail page renders with the Karta tab, click Bojovka tab, verify HP counter local state increments and resets. Check Kořist tab shows per-game grouping.

## PR workflow

```
git checkout -b feat/monster-detail-port main
# ... implement ...
dotnet build OvcinaHra.slnx
dotnet test
git add -p && git commit   # [v0.12.x] tag in commit msg per skill
git push -u origin feat/monster-detail-port
gh pr create --base main --head feat/monster-detail-port \
  --title "feat(monsters): /monsters/{id} detail page with 5 tabs + Bojovka presenter [v0.12.x]" \
  --body "See docs/design/dialogs/monster-detail.md and docs/port-handoffs/hra-ovcina-monster-detail-port.md for decisions. Illuminated bestiary page with Karta, Upravit, Bojovka (presenter view), Kořist (per-game MTG loot tiles), and Výskyt tabs. Uses existing MonsterDetailDto and MonsterLootDto; add aggregate loot endpoint if needed."
```

Squash-merge with `gh pr merge <N> --squash --delete-branch` after CI + Copilot. Verify deploy via `curl https://api.hra.ovcina.cz/api/version`.

## Gotchas

- **`TreatWarningsAsErrors=true`** — watch out in the Razor: pattern-match `@if (x is int y)` warns when `y` unused; prefer `.HasValue` / `is not null`.
- **`CombatStats` is a value object** (Attack/Defense/Health). On Upravit, edit flat in DxSpinEdit; rebuild the VO in the save handler before PUT.
- **`Category` is an int** (level/rank 0-10). Clamp in DxSpinEdit.
- **Bojovka HP counter is LOCAL ONLY** — do NOT wire to any API. A `private int tempHp` in @code, reset to `loc!.Stats.Health` on tab enter or when "resetovat" is clicked. This is advisory for the organizer running the encounter live.
- **MonsterType chip must NOT carry kingdom colors** (MEMORY `feedback_ovcina_kingdom_seal_palette`). Use the neutral chip palette — same as ItemType elsewhere.
- **Tags:** `MonsterDetailDto` has `List<TagDto> Tags` (not the flattened `TagsDisplay` from the list DTO). Use `Tags` directly.
- **Kořist endpoint may not exist yet** — `MonsterLootDto` shape is already defined. If `/api/monsters/{id}/loot?gameId=X` isn't there, extend `MonsterEndpoints` before wiring the tab. Add Testcontainers integration test.
- **"Odebrat z hry" confirm** — the header text matters: "Odebrat {Name} z hry Ovčina 2023?" — interpolate the game name so the confirmation is unambiguous. The mockup uses this exact phrasing.
- **"+ Přidat kořist"** opens an Item picker + quantity input. Inline popup (tiny DxPopup with a searchable item combo + DxSpinEdit quantity). POSTs `CreateMonsterLootDto`, refreshes the tab.
- **`PhysicalForm` (enum)** shows on Item chips — not on Monsters. Don't confuse the two.

## Follow-ups

1. **Playwright E2E** covering navigation into detail + Bojovka counter + Kořist tab.
2. **Aggregate occurrences endpoint** `/api/monsters/{id}/occurrences` bundling QuestEncounter and GameMonster counts — feeds the Výskyt summary. Add if it doesn't exist.
3. **Extract `.oh-stat-pill-*`** base rules shared between MonsterList (5 inline pills) and MonsterDetail Bojovka (3 big stat boxes) — if the duplication becomes obvious. Not required in this PR.
4. **TagChipPicker** — if extracted, plumb it into Items and Quests in a follow-up PR.

## Companion references

- Brief: `docs/design/dialogs/monster-detail.md`
- Sibling list brief: `docs/design/dialogs/monster-list.md`
- Prior port for reference: `docs/port-handoffs/hra-ovcina-monster-list-port.md`
- MTG tile pattern: `src/OvcinaHra.Client/Components/LocationStashTile.razor`
- Design system onboarding: `docs/design/CLAUDE-DESIGN-SETUP.md`
- Builder template: `docs/design/BUILDER-BRIEF-TEMPLATE.md`
