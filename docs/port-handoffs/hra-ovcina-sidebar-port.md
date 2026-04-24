# Handoff — port the sidebar redesign (2026-04-23)

Load the `hra-ovcina-tinkerer` skill first. Then read this file before touching code.

## What this is

Claude Design shipped **Variant C — záložky (tabs)** for the sidebar nav. The designer wrote implementation notes directly into the mockup (unusual + very useful). Your job is to port it to production Blazor on a new branch and ship it via PR.

- **Bundled mockup:** `C:\Users\TomášPajonk\Downloads\Sidebar - Final _standalone_ (1).html` (2.5 MB, gitignored when copied in).
- **Decoded HTML:** copy the bundled file into `docs/design/dialogs/sidebar.mockup.html` (gitignored), then decode via:
  ```python
  import re, json
  with open("docs/design/dialogs/sidebar.mockup.html", encoding="utf-8") as f: h = f.read()
  with open("docs/design/dialogs/sidebar.mockup.unbundled.html", "w", encoding="utf-8") as o:
      o.write(json.loads(re.search(r'<script type="__bundler/template"[^>]*>(.*?)</script>', h, re.S).group(1).strip()))
  ```

**Note:** This mockup is a **React component**, not raw HTML. The state logic (`mode === 'hra'`, `useState`, `localStorage` persistence) will port directly to Blazor as plain C# state in `NavMenu.razor` — no React needed. Only the markup structure + CSS get copied.

## Designer's notes (verbatim from the mockup's "Co se změnilo" section)

> **Záložky „Tato hra" / „Katalog"** replace two duplicate sections. You see EITHER this year's game OR templates across years — not both at once.
>
> **Přehled** and **Mapa** stay at the top — not tied to mode.
>
> **Nástroje** is anchored at the bottom with a darkened background (it's meta, not content).
>
> Active item: **3 px green left stripe**, sage gradient, white bolder typography, subtle parchment **•** on the right ("you are here").
>
> Inactive tab has a **wrapped top corner** — an actual paper tab, not just a different bg color.
>
> Mode preference is remembered in **localStorage** so F5 doesn't lose the user's work-in-progress.
>
> "Sidebar structure is 1:1 with NavMenu.razor — CSS just needs to be added to NavMenu.razor.css."

## Decisions the designer locked

| Area | Choice |
|---|---|
| Mode switch | **Tabs at the top** (not a segmented control elsewhere). Two tabs: "Tato hra" / "Katalog". Inactive tab has a wrapped/angled top corner like a paper tab sticking out. |
| Meta line | Under the tab row. For "Tato hra" mode: active game edition name + month (e.g. "Ovčina 2026 — Stíny Rhovanionu · květen 2026"). For "Katalog" mode: "sdílené napříč ročníky". |
| Fixed top | Přehled + Mapa stay visible in both modes (they're not game-scoped nor catalog-scoped). |
| Scrollable middle | Mode-dependent item list. Items crossfade on tab switch. |
| Fixed bottom | Nástroje section (darker bg, sticky). |
| Active-item styling | 3 px forest-green left stripe · sage-mist gradient bg · white 600-weight text · small parchment-cream `•` pinned to the right edge ("you are here") |
| Persistence | `mode` value in `localStorage` — survives F5 |
| Collapsed (60 px) variant | **Out of scope for this port.** Designer offered it as follow-up — open an issue; don't try to build it in this PR. |

## NAV lists (port 1:1 from existing `NavMenu.razor`)

Existing hrefs stay exactly as they are. Don't invent new routes. The mockup's NAV_HRA and NAV_KATALOG arrays are guides — map them to the routes already in `NavMenu.razor`.

**NAV_HRA (mode = "Tato hra"):**
```
Časová osa         timeline            bi-clock-history
Poklady            treasures           bi-safe-fill
Postavy            characters          bi-person-fill
Události           events              bi-calendar-event
Lokace             locations           bi-geo-alt-fill
Předměty           items               bi-gem
Příšery            monsters            (local SVG: img/troll.svg)
Questy             quests              bi-journal-text
Osobní questy      personal-quests     bi-journal-bookmark
Budovy             buildings           bi-houses
Dovednosti         games/{id}/skills   bi-star-fill
Kouzla             games/{id}/spells   bi-magic
Tajné skrýše       secret-stashes      bi-lock-fill
NPC                npcs                bi-person-badge-fill
```

**NAV_KATALOG (mode = "Katalog"):**
```
Lokace             locations?catalog=true         bi-geo-alt
Předměty           items?catalog=true             bi-gem
Příšery            monsters?catalog=true          (local SVG: troll.svg)
Questy             quests?catalog=true            bi-journal-text
Osobní questy      personal-quests?catalog=true   bi-person-workspace
Budovy             buildings?catalog=true         bi-houses
Dovednosti         skills                         bi-star
Kouzla             spells                         bi-magic
Tajné skrýše       secret-stashes?catalog=true    bi-lock
NPC                npcs?catalog=true              bi-person-badge
Království         kingdoms                       bi-flag
Tagy               tags                           bi-tags
```

**Always visible (top of sidebar, both modes):**
```
Přehled            /                   bi-feather
Mapa               map                 bi-map-fill
```

**Bottom anchored (Nástroje):**
```
Seznam Ovčin       games               bi-calendar-event-fill
Import             import              bi-box-arrow-in-down
Nastavení          settings            bi-gear-fill
```

Compare against current `src/OvcinaHra.Client/Layout/NavMenu.razor` for the canonical href list — the current file is the source of truth for routes. If a route differs from the above table, trust the current file.

## Starting state

### Branches
- `main` currently at v0.12.1 with location redesign merged.
- **Your new branch:** `feat/sidebar-redesign` off latest `main`.

### Files to modify
- `src/OvcinaHra.Client/Layout/NavMenu.razor` — rewrite with the new tab structure.
- `src/OvcinaHra.Client/Layout/NavMenu.razor.css` — isolated CSS; append/replace with the sidebar's scoped rules. Prefix any new classes with `.oh-nav-*`.
- `src/OvcinaHra.Client/Layout/MainLayout.razor` — likely no change; the sidebar fits the existing frame. Verify.
- `src/OvcinaHra.Client/wwwroot/js/nav-mode.js` (NEW, small) — 3-line helper for localStorage get/set so the component can read the stored mode on first render without a flash-of-wrong-content.

### State to add to NavMenu.razor
```csharp
private string mode = "hra";   // "hra" | "katalog"

protected override async Task OnAfterRenderAsync(bool firstRender)
{
    if (firstRender)
    {
        var stored = await JS.InvokeAsync<string?>("localStorage.getItem", "oh-nav-mode");
        if (stored == "hra" || stored == "katalog") { mode = stored; StateHasChanged(); }
    }
}

private async Task SwitchMode(string next)
{
    mode = next;
    await JS.InvokeVoidAsync("localStorage.setItem", "oh-nav-mode", next);
}
```

## Acceptance criteria

- [ ] `dotnet build OvcinaHra.slnx` clean, zero new warnings (`TreatWarningsAsErrors=true`)
- [ ] `dotnet test` green (no API changes)
- [ ] Sidebar shows two tabs at top: `Tato hra` / `Katalog` — click swaps the item list
- [ ] Přehled + Mapa stay visible in both modes at the top
- [ ] Nástroje section is pinned to the bottom with darker bg
- [ ] Active nav item has: 3 px forest-green left stripe · sage gradient bg · white 600-weight text · right-side parchment dot
- [ ] Inactive tab has a wrapped top corner (paper-tab effect)
- [ ] Meta line under tab row shows the active game name/edition when mode=hra; "sdílené napříč ročníky" when mode=katalog
- [ ] After tab click, items crossfade (short CSS opacity transition is fine — don't over-engineer)
- [ ] Mode survives F5 (localStorage round-trip working)
- [ ] All existing routes still resolve (no 404s when clicking nav items)
- [ ] Czech diacritics render cleanly
- [ ] DevTools console clean

## Testing expectations

- **Integration tests:** no API changes — run `dotnet test` to confirm nothing regressed.
- **Playwright E2E:** add a tiny test that visits `/`, clicks the `Katalog` tab, verifies a catalog-only nav item ("Království") becomes visible, then reloads the page and verifies the Katalog tab is still selected. Reference existing tests in `tests/OvcinaHra.E2E/`.

## PR workflow

```
git checkout -b feat/sidebar-redesign main
# ... implement ...
dotnet build OvcinaHra.slnx
dotnet test
git add -p && git commit   # [v0.12.x] tag in commit msg per skill
git push -u origin feat/sidebar-redesign
gh pr create --base main --head feat/sidebar-redesign \
  --title "feat(nav): Tato hra / Katalog tabs in sidebar [v0.12.x]" \
  --body "Variant C sidebar redesign from Claude Design mockup. Collapses the old duplicate Hra/Katalog sections into a tab switch at the top of the sidebar. Persists mode in localStorage. Přehled+Mapa always visible; Nástroje anchored at bottom. See docs/port-handoffs/hra-ovcina-sidebar-port.md for the full decision log."
```

Wait for CI + Copilot review. Fixup commit if needed. Merge with `gh pr merge <N> --squash --delete-branch`. Verify deploy via `curl https://api.hra.ovcina.cz/api/version` after ~8 min.

## Gotchas

- **React code in the mockup ≠ your implementation.** The mockup uses `mode === 'hra' ? NAV_HRA : NAV_KATALOG`. In Blazor, use a C# property + `@if (mode == "hra")` branches. Don't try to port `useState` or `ReactDOM.render`.
- **Existing CSS for nav lives in `NavMenu.razor.css`** (Blazor scoped CSS). When you append new rules, they auto-scope. Prefix your new class names with `.oh-nav-*` anyway — consistency with the rest of the theme.
- **`MainLayout.razor` holds the sidebar width** (250 px) and toggle state. Confirm the new NavMenu fits within the existing frame before touching layout.
- **localStorage access during prerender** throws — use `OnAfterRenderAsync(firstRender)` not `OnInitializedAsync`.
- **Tab wrapped-corner effect:** use CSS `border-top-left-radius` on the inactive-side + `clip-path` or overflow masking for the active tab's curved cut. The designer's CSS in the decoded mockup is the reference — copy it straight.
- **Active game edition name** on the meta line under tabs — pull from `GameContextService.GameName`. Null-handle for "no active game" state.
- **bi-feather** is the brand glyph — used for Přehled AND Mapa currently; pick `bi-house-door-fill` for Přehled and `bi-map-fill` for Mapa (verify against current NavMenu — those are what's shipping today).
- **DON'T rename routes.** The mockup labels are design intent; the existing hrefs in NavMenu.razor are the canonical routes.

## Follow-ups (create issues or stack)

1. **Collapsed 60 px variant** — icons only, mode toggle via top icon. Designer offered to ship this as Phase 2. Open an issue tagged `ux-sidebar-collapse`.
2. **Keyboard nav** — Tab/arrow key traversal of the sidebar items. Accessibility.
3. **Search inside sidebar** — for organizers with 20+ nav items, a fuzzy "Najdi stránku…" field above the list would help. Defer.

## Companion references

- Current sidebar source: `src/OvcinaHra.Client/Layout/NavMenu.razor`
- Current sidebar CSS: `src/OvcinaHra.Client/Layout/NavMenu.razor.css`
- Design system onboarding: `docs/design/CLAUDE-DESIGN-SETUP.md`
- Builder template: `docs/design/BUILDER-BRIEF-TEMPLATE.md`
- Prior port for reference: `docs/port-handoffs/hra-ovcina-secret-stash-list-port.md`, `docs/port-handoffs/hra-ovcina-monster-list-port.md`
- GameContextService (for the meta line under tabs): `src/OvcinaHra.Client/Services/GameContextService.cs`
