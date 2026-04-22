# Skills Page Pattern Alignment — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Merge Skills UI into a single `/skills` page with `Catalog` query param + `GameContextService`, matching the Items pattern. Remove the separate `/games/{id}/skills` route and its Game-edit-popup entry point.

**Architecture:** One Razor page (`Pages/Skills/Skills.razor`) rewritten to mirror `Pages/Items/ItemList.razor`. One Razor page deleted (`Pages/Games/GameSkills.razor`). Small surgical edits to `NavMenu.razor` and `GameList.razor`. No API changes, no domain changes.

**Tech Stack:** Blazor WASM, DevExpress Blazor grid + popup + form components, existing `SkillService`, `GameContextService`, `OvcinaGrid` wrapper.

**Design doc:** `docs/plans/2026-04-19-skills-page-pattern-align-design.md` — read first.

**Project conventions:**
- Code in English, UI text in Czech with diacritics
- Hrdina/Hrdinové for player references (none needed in this page but keep in mind)
- "Dobrodruh" for adventurer-skill category label (from `PlayerClassExtensions.GetClassRestrictionLabel()`)
- Commit after every task; include `[v0.5.4]` in commit messages per project convention
- Run client build after each change: `dotnet build src/OvcinaHra.Client/OvcinaHra.Client.csproj` — 0 errors

---

## Task 1 — Rewrite `Skills.razor` as unified catalog/in-game page

**Files:**
- Modify: `src/OvcinaHra.Client/Pages/Skills/Skills.razor` (wholesale rewrite)

**Reference patterns (READ FIRST):**
- `src/OvcinaHra.Client/Pages/Items/ItemList.razor` — the template to mirror. Note:
  - `[Parameter, SupplyParameterFromQuery] public bool Catalog { get; set; }`
  - Two toggle buttons at the top (Hra / Katalog)
  - Empty state when `!Catalog && GameContext.SelectedGameId is null`
  - Unified detail popup with conditional per-game section
  - `OnParametersSetAsync` reloads data when `Catalog` changes
- `src/OvcinaHra.Client/Pages/Games/GameSkills.razor` — the per-game view being replaced. Salvage the add-to-game flow + XP/Level edit logic.
- The existing `Skills.razor` (catalog-only view) — salvage the detail popup for global fields + building-requirements list.

**Subagent instructions:** read both template files in full before writing. The new `Skills.razor` should combine Items' architecture (routing, toggle, empty state) with the existing Skills.razor's catalog editing logic and GameSkills.razor's in-game editing logic.

### High-level structure

```razor
@page "/skills"
@attribute [Authorize]
@inject SkillService SkillSvc
@inject GameContextService GameContext
@inject NavigationManager Nav

<div class="d-flex justify-content-between align-items-center mb-3">
    <h1 class="mb-0">Dovednosti</h1>
    <div>
        <button class="btn btn-outline-secondary @(!Catalog ? "active" : "")"
                @onclick='() => Nav.NavigateTo("/skills")'>Hra</button>
        <button class="btn btn-outline-secondary @(Catalog ? "active" : "")"
                @onclick='() => Nav.NavigateTo("/skills?catalog=true")'>Katalog</button>
        <button class="btn btn-primary ms-2" @onclick="OpenAdd">
            + Přidat dovednost
        </button>
    </div>
</div>

@if (loading)
{
    <p>Načítám...</p>
}
else if (!Catalog && GameContext.SelectedGameId is null)
{
    <div class="text-center text-muted py-5">
        <p>Žádná aktivní hra.</p>
        <button class="btn btn-outline-primary" @onclick='() => Nav.NavigateTo("/skills?catalog=true")'>
            Otevřít katalog
        </button>
    </div>
}
else if (Catalog)
{
    <OvcinaGrid Data="@catalogSkills" KeyFieldName="Id" LayoutKey="grid-layout-skills-catalog" ...>
        <!-- Name, ClassRestriction, Effect, Buildings, Notes -->
    </OvcinaGrid>
}
else
{
    <OvcinaGrid Data="@gameSkills" KeyFieldName="SkillId" LayoutKey="grid-layout-skills-game" ...>
        <!-- Name, ClassRestriction, XpCost, LevelRequirement, remove action -->
    </OvcinaGrid>
}

<!-- Unified detail popup with conditional per-game section -->
<DxPopup @bind-Visible="popupVisible" ...>
    <!-- Global fields section (always) -->
    <!-- Per-game section (only when !Catalog && editingId.HasValue) -->
</DxPopup>
```

### Key fields on the component

```csharp
[Parameter, SupplyParameterFromQuery]
public bool Catalog { get; set; }

private bool loading = true;
private bool _catalogLoaded;
private bool _gameLoaded;
private int? _lastLoadedGameId;

private List<SkillDto> catalogSkills = [];
private List<GameSkillDto> gameSkills = [];
private List<BuildingListDto> allBuildings = [];

// Edit popup state
private bool popupVisible;
private bool isAdding;
private int? editingId; // SkillId being edited (or null when adding new)

// Global-fields form state
private string editName = "";
private SkillKind editKind = SkillKind.Adventurer;
private PlayerClass? editClassRestriction;
private string editEffect = "";
private string editNotes = "";
private List<int> editBuildingIds = [];

// Per-game form state (in-game mode only)
private int editXpCost;
private int? editLevelRequirement;

// Add-to-game picker state
private int? pickerSelectedSkillId;

// Delete confirmation
private bool deletePopupVisible;

private enum SkillKind { ClassSpecific, Adventurer }
```

### Key lifecycle behavior

- `OnInitializedAsync`: `await GameContext.InitializeAsync();` then `await LoadData();` then `loading = false`.
- `OnParametersSetAsync`: when `Catalog` changes or `SelectedGameId` changes since last load, call `LoadData()` again. Track via `_catalogLoaded` / `_gameLoaded` / `_lastLoadedGameId` to avoid redundant fetches on unrelated re-renders (same caching pattern as Items).
- `LoadData()` reads skills in the right mode:
  - Catalog mode → `catalogSkills = await SkillSvc.GetAllAsync();`
  - In-game mode → `gameSkills = await SkillSvc.GetGameSkillsAsync(gameId);`
  - Always load `allBuildings` once for the building-picker in the popup.

### Save / delete logic (full text — use verbatim)

```csharp
private async Task SaveAsync()
{
    try
    {
        var buildingIds = editBuildingIds.Distinct().ToList();
        var classRestriction = editKind == SkillKind.ClassSpecific ? editClassRestriction : null;

        int skillId;
        if (isAdding && Catalog)
        {
            // Create global skill
            var req = new CreateSkillRequest(editName, classRestriction, editEffect, editNotes, buildingIds);
            var created = await SkillSvc.CreateAsync(req);
            skillId = created.Id;
        }
        else if (editingId.HasValue)
        {
            // Update existing global skill
            var req = new UpdateSkillRequest(editName, classRestriction, editEffect, editNotes, buildingIds);
            await SkillSvc.UpdateAsync(editingId.Value, req);
            skillId = editingId.Value;
        }
        else
        {
            throw new InvalidOperationException("unreachable");
        }

        // In-game mode: also save per-game XP/Level (always — treat as upsert)
        if (!Catalog && GameContext.SelectedGameId is int gid)
        {
            var gsReq = new UpsertGameSkillRequest(editXpCost, editLevelRequirement);
            await SkillSvc.UpsertGameSkillAsync(gid, skillId, gsReq);
        }

        popupVisible = false;
        await LoadData();
    }
    catch (Exception ex)
    {
        popupError = ex.Message;
    }
}

private async Task DeleteConfirmedAsync()
{
    try
    {
        if (Catalog)
        {
            await SkillSvc.DeleteAsync(editingId!.Value);
        }
        else if (GameContext.SelectedGameId is int gid)
        {
            await SkillSvc.RemoveGameSkillAsync(gid, editingId!.Value);
        }
        deletePopupVisible = false;
        popupVisible = false;
        await LoadData();
    }
    catch (Exception ex)
    {
        popupError = ex.Message;
    }
}
```

### Add-to-game flow (in-game + isAdding)

Instead of a plain `+ Přidat` that creates a new global skill, in-game mode's add button opens a **picker popup** first:
- `DxComboBox` bound to `allCatalogSkillsNotInGame = (await SkillSvc.GetAllAsync()).Where(s => !gameSkills.Any(gs => gs.SkillId == s.Id))`.
- User picks skill, then fills XP/Level, then saves → `UpsertGameSkillAsync(gid, skillId, ...)`.

The global-fields popup is NOT used in this add-to-game flow (the skill already exists globally — we're just activating it in the game).

Implementer judgment: this can be a second `DxPopup` for the picker, OR the same detail popup with conditional add-vs-edit state. Choose whichever keeps the code readable. Items uses two popups for its add-ingredient flow — same approach works here.

### Steps

**Step 1:** Read `src/OvcinaHra.Client/Pages/Items/ItemList.razor` in full. Note the structure, the parameter caching, the empty state, the popup layout. Spend time here — it's the template.

**Step 2:** Read the current `src/OvcinaHra.Client/Pages/Skills/Skills.razor` and `src/OvcinaHra.Client/Pages/Games/GameSkills.razor` to understand what logic exists to be merged. Salvage Czech strings verbatim — don't invent new ones.

**Step 3:** Rewrite `Skills.razor` wholesale. Keep the file tight but readable. No JS interop needed — this is pure Blazor + DevExpress.

**Step 4:** Build:
```
dotnet build src/OvcinaHra.Client/OvcinaHra.Client.csproj
```
Expected: 0 errors. Warnings OK only if pre-existing.

**Step 5:** Commit:
```bash
git add src/OvcinaHra.Client/Pages/Skills/Skills.razor
git commit -m "feat(skills): unify /skills to catalog+in-game page mirroring Items [v0.5.4]"
```

---

## Task 2 — Delete `GameSkills.razor` and its Game-popup entry point

**Files:**
- Delete: `src/OvcinaHra.Client/Pages/Games/GameSkills.razor`
- Modify: `src/OvcinaHra.Client/Pages/Games/GameList.razor` — remove the "Dovednosti" button + its handler + the `NavigationManager` injection if now unused.

**Step 1:** Delete `GameSkills.razor`:
```bash
git rm src/OvcinaHra.Client/Pages/Games/GameSkills.razor
```

**Step 2:** In `GameList.razor`:
- Search for the "Dovednosti" button in the edit popup footer — remove it.
- Remove the `GoToSkills()` (or similarly-named) handler method.
- Check whether `NavigationManager` is still used anywhere in the file:
  - If yes → keep the `@inject`.
  - If no → remove the `@inject NavigationManager Nav` line.
- Don't touch anything else. Whitespace cleanup only where removing lines leaves awkward gaps.

**Step 3:** Build:
```
dotnet build src/OvcinaHra.Client/OvcinaHra.Client.csproj
```
Expected: 0 errors.

**Step 4:** Commit:
```bash
git add src/OvcinaHra.Client/Pages/Games/
git commit -m "feat(skills): remove standalone GameSkills route and entry point [v0.5.4]"
```

---

## Task 3 — Update nav links

**File:** `src/OvcinaHra.Client/Layout/NavMenu.razor`

Current state (from earlier investigation):
- Line 54 area: Hra section "Dovednosti" — `href="skills"` — CORRECT, leave alone.
- Line 116 area: Katalog světa section "Dovednosti" — `href="skills"` — WRONG, needs `?catalog=true`.

**Step 1:** Find the Katalog-světa-section Dovednosti entry. Change `href="skills"` to `href="skills?catalog=true"`.

Before:
```razor
<NavLink class="nav-link" href="skills">
    <i class="bi bi-star"></i><span class="nav-label">Dovednosti</span>
</NavLink>
```
After (Katalog section only):
```razor
<NavLink class="nav-link" href="skills?catalog=true">
    <i class="bi bi-star"></i><span class="nav-label">Dovednosti</span>
</NavLink>
```

**IMPORTANT:** There are TWO "Dovednosti" links in this file. Do not change both — only the one in the Katalog světa section. The Hra-section link stays as `href="skills"` so it opens the in-game mode.

**Step 2:** Build:
```
dotnet build src/OvcinaHra.Client/OvcinaHra.Client.csproj
```
Expected: 0 errors.

**Step 3:** Commit:
```bash
git add src/OvcinaHra.Client/Layout/NavMenu.razor
git commit -m "feat(skills): point Katalog-světa nav link to ?catalog=true [v0.5.4]"
```

---

## Task 4 — Manual browser smoke test

Run `devStart.bat`, then go through each scenario. Report any layout/UX issue.

### Scenarios

1. **Navigate to `/skills` with an active game.** Expect: in-game mode grid with Name/Class/XP/Level columns. GameSkill rows only.
2. **Click the Katalog toggle.** URL becomes `/skills?catalog=true`. Grid switches to global catalog columns (no XP/Level).
3. **In in-game mode, click `+ Přidat dovednost`.** Expect: picker popup with catalog skills not yet in the game. Pick one → XP/Level form → save → new row appears.
4. **In in-game mode, click a row.** Expect: detail popup with global fields (editable) + per-game XP/Level (editable). Change XP → save → grid reflects new value.
5. **In in-game mode, click the remove (×) action on a row referenced by a recipe.** Expect: Czech 409 message surfaced, row stays.
6. **In catalog mode, click `+ Přidat dovednost`.** Expect: detail popup without per-game section. Create new skill → save → appears in list.
7. **In catalog mode, click delete on a referenced skill.** Expect: Czech 409 message, skill stays.
8. **Navigate to `/skills` with NO active game.** Expect: empty-state prompt "Žádná aktivní hra." + "Otevřít katalog" button → click → URL becomes `/skills?catalog=true`.
9. **From the nav menu, click Hra → Dovednosti.** Expect: `/skills` (in-game). Click Katalog světa → Dovednosti. Expect: `/skills?catalog=true`.
10. **Open a game from `/games` — the edit popup.** Confirm there's NO longer a "Dovednosti" button (removed in Task 2).

Fixes for any issue found go in the appropriate task's file, committed separately:
```
git commit -m "fix(skills): <specific issue>  [v0.5.4]"
```

---

## Completion checklist

- [ ] Task 1 — unified `/skills` page committed
- [ ] Task 2 — `GameSkills.razor` deleted + `GameList.razor` entry point removed
- [ ] Task 3 — NavMenu Katalog link updated
- [ ] Task 4 — manual smoke passed on all 10 scenarios
- [ ] `dotnet build src/OvcinaHra.Client/OvcinaHra.Client.csproj` → 0 errors
- [ ] `grep -rn "games/{GameId:int}/skills\|GameSkills.razor\|GoToSkills" src/OvcinaHra.Client` → zero matches (except `.cs` navigation helpers if any)
- [ ] Version bump to `0.5.4` in `OvcinaHra.Client.csproj` when ready to deploy

## Notes for the implementer

- This is a UI-only change. No API, no DB, no domain.
- The refactor hinges on reading `ItemList.razor` carefully — it's the template. If you try to write this from scratch without reading that file, you'll miss subtleties (especially the parameter-caching logic that prevents data-reload spam).
- All `SkillService` methods used here already exist from the skills-domain PR — no service changes needed.
- Czech strings should be **salvaged** from the current `Skills.razor` and `GameSkills.razor` files, not reinvented. Look for "Požadavky na povolání", "Nastavení pro aktuální hru", "Cena v XP", "Požadovaná úroveň", "Bez požadavku", "Přidat dovednost ze seznamu" — reuse them where they fit.
- The `SkillKind` enum (ClassSpecific vs Adventurer) is a UI-only helper for the radio-toggle — do not add it to the shared DTOs.
- Layout keys for `OvcinaGrid`: use `"grid-layout-skills-catalog"` and `"grid-layout-skills-game"` so the two views have independent persisted layouts (same pattern as Items).
