# Quest Maker Wizard — Implementation Plan (Phase 2)

> **For Claude:** Use superpowers:subagent-driven-development to execute.

**Design:** `docs/plans/2026-04-23-personal-quest-wizard-design.md` (required reading)

**Goal:** Ship a create-quest wizard launched from the game-quests view, with inline creators for the 3 reward types that also auto-link the new catalog entity to the current game.

**Architecture:** Pure client-side. One big `DxPopup` with a smart form in `QuestWizard.razor`, three small nested `DxPopup` inline creators (Skill/Item/Spell), sequential client-side save flow calling existing Phase 1 endpoints. Zero new server code, zero migrations.

**Tech stack:** Blazor WASM + DevExpress Blazor 25.2.5. `ApiClient` wrapper. `GameContextService` for active-game id.

**Branch:** `feat/personal-quest-wizard` (design doc already committed).

**Project conventions:**
- Every commit subject ends `[v0.10.1]`.
- UI strings Czech with diacritics. Identifiers English.
- `dotnet build` must be green at every task boundary.
- No new API endpoints. No migrations. No schema changes.
- Reuse existing `CreateSkillDto` / `CreateItemDto` / `CreateSpellDto` / `CreateGameSkillDto` / `CreateGameItemDto` / `CreateGameSpellDto` — do not invent new DTOs.
- `Api.PostAsync<TRequest, TResponse>(path, body)` + `Api.DeleteAsync(path)` patterns established in `PersonalQuestList.razor`.
- Destructive UI actions need `DxPopup` confirmations; reward badges follow the existing "no-confirm" pattern (match Items).

**Working directory:** Repository root (`.`).

---

## Task 1 — Inline creator trio (Skill, Item, Spell)

All three follow an identical pattern. One implementer dispatch. One commit.

**Files to create:**
- `src/OvcinaHra.Client/Pages/PersonalQuests/SkillInlineCreator.razor`
- `src/OvcinaHra.Client/Pages/PersonalQuests/ItemInlineCreator.razor`
- `src/OvcinaHra.Client/Pages/PersonalQuests/SpellInlineCreator.razor`

**Each component shape:**

```razor
@* SkillInlineCreator.razor — template *@
@inject ApiClient Api
@using OvcinaHra.Shared.Dtos
@using OvcinaHra.Shared.Domain.Enums

<DxPopup @bind-Visible="@Visible" HeaderText="Nová dovednost" Width="640px">
    <BodyContentTemplate Context="ctx">
        <DxFormLayout>
            @* fields — mirror the Skills.razor create popup — minimum required
               for a reward-grade entity: Name, Category, optional Effect, optional
               ClassRestriction (conditional on Category=Class) *@
        </DxFormLayout>
        @if (error is not null) { <div class="alert alert-danger mt-2">@error</div> }
        <div class="d-flex gap-2 justify-content-end mt-3">
            <button class="btn btn-secondary" @onclick="Cancel" disabled="@isSaving">Zrušit</button>
            <button class="btn btn-primary" @onclick="SaveAsync" disabled="@isSaving">
                @if (isSaving) { <span class="spinner-border spinner-border-sm me-1"></span> }
                Vytvořit a přidat
            </button>
        </div>
    </BodyContentTemplate>
</DxPopup>

@code {
    [Parameter] public bool Visible { get; set; }
    [Parameter] public EventCallback<bool> VisibleChanged { get; set; }
    [Parameter] public int GameId { get; set; }
    [Parameter] public EventCallback<SkillDto> Created { get; set; }

    // local model fields + isSaving + error

    private async Task SaveAsync()
    {
        error = null; isSaving = true;
        try
        {
            var created = await Api.PostAsync<CreateSkillRequest, SkillDto>(
                "/api/skills", new CreateSkillRequest(/* fields */));

            // Auto-link to current game per design decision Q1/B
            await Api.PostAsync<CreateGameSkillDto, object?>(
                "/api/skills/game-skill",
                new CreateGameSkillDto(GameId, created.Id /* + defaults if applicable */));

            await Created.InvokeAsync(created);
            await VisibleChanged.InvokeAsync(false);
        }
        catch (Exception ex) { error = ex.Message; }
        finally { isSaving = false; }
    }

    private async Task Cancel() => await VisibleChanged.InvokeAsync(false);
}
```

**Per-entity specifics:**

- **Skill**: fields Name, Category (`DxComboBox` with `SkillCategory` values), ClassRestriction (conditional on `Category == Class`), Effect, RequirementNotes. Use `CreateSkillRequest` and endpoints already used by `Skills.razor`. Link via `/api/skills/game-skill` (match payload shape used in `SkillService` or existing code — inspect before implementing).
- **Item**: fields Name, ItemType (combo), Description, Price (nullable), IsSold checkbox auto-toggles with Price, StockCount. Use `CreateItemDto`. Link via `/api/items/game-item` with `CreateGameItemDto(gameId, itemId)` defaults.
- **Spell**: fields Name, Level, ManaCost, School (combo), IsScroll, IsReaction, MinMageLevel, Price (nullable), Effect, Description. **`IsLearnable` is not exposed — force `false` server-side** (set `IsLearnable = false` in the DTO before POST; since non-learnable is required for quest rewards per Phase 1). Link via `/api/spells/game-spell`.

**Verification:** `dotnet build` clean.

**Commit:**
```bash
git add src/OvcinaHra.Client/Pages/PersonalQuests/SkillInlineCreator.razor \
        src/OvcinaHra.Client/Pages/PersonalQuests/ItemInlineCreator.razor \
        src/OvcinaHra.Client/Pages/PersonalQuests/SpellInlineCreator.razor
git commit -m "feat(quest-wizard): inline creators for skill/item/spell rewards [v0.10.1]"
```

**Before starting this task, read:**
- `src/OvcinaHra.Client/Pages/Skills/Skills.razor` — see the current Create-skill popup to know exactly which fields matter
- `src/OvcinaHra.Client/Pages/Items/ItemList.razor` — see Create-item popup
- `src/OvcinaHra.Client/Pages/Spells/SpellList.razor` — see Create-spell popup
- `src/OvcinaHra.Client/Services/SkillService.cs` / `src/OvcinaHra.Client/Services/PersonalQuestService.cs` — see if there are wrapper methods to reuse rather than raw `Api.PostAsync`

---

## Task 2 — `QuestWizard.razor` component

The meat of the wizard. One implementer dispatch. One commit.

**Files to create:**
- `src/OvcinaHra.Client/Pages/PersonalQuests/QuestWizard.razor`

**Public parameters:**
```csharp
[Parameter] public bool Visible { get; set; }
[Parameter] public EventCallback<bool> VisibleChanged { get; set; }
[Parameter] public int GameId { get; set; }
[Parameter] public int CatalogXpFallback { get; set; }  // display-only for the per-game hint
[Parameter] public EventCallback Saved { get; set; }   // parent reloads list
```

**Shell:**
```razor
<DxPopup @bind-Visible="@Visible" HeaderText="Vytvořit nový personal quest"
         Width="900px" AllowResize="true" HeaderCssClass="oh-wizard-header">
    <BodyContentTemplate Context="ctx">
        <!-- 4 form sections, see design doc "Form sections" -->
        <!-- Rewards section renders the 3 reward-type sub-blocks, each with
             Existing-entry combobox + "Vytvořit novou…" button that toggles
             an inline creator -->
        <!-- Save flow UI: progress indicator + error alert + "Uložit a přidat
             do hry" button + "Zrušit" -->
    </BodyContentTemplate>
</DxPopup>

<SkillInlineCreator @bind-Visible="@skillCreatorVisible" GameId="@GameId"
                    Created="@OnSkillCreated" />
<ItemInlineCreator  @bind-Visible="@itemCreatorVisible"  GameId="@GameId"
                    Created="@OnItemCreated" />
<SpellInlineCreator @bind-Visible="@spellCreatorVisible" GameId="@GameId"
                    Created="@OnSpellCreated" />
```

**View-model sketch:**
```csharp
private WizardModel model = new();

private bool skillCreatorVisible, itemCreatorVisible, spellCreatorVisible;
private List<SkillDto> allSkills = [];
private List<ItemListDto> allItems = [];
private List<SpellListDto> allSpells = [];  // filtered to IsLearnable=false
private int? pickedSkillId, pickedItemId, pickedSpellId;
private int pickedItemQty = 1, pickedSpellQty = 1;

private string progressLabel = "";
private bool isSaving;
private string? error;

protected override async Task OnParametersSetAsync()
{
    if (Visible && allSkills.Count == 0)
    {
        allSkills = await Api.GetListAsync<SkillDto>("/api/skills");
        allItems  = await Api.GetListAsync<ItemListDto>("/api/items");
        var spells = await Api.GetListAsync<SpellListDto>("/api/spells");
        allSpells = spells.Where(s => !s.IsLearnable).ToList();
    }
}

private sealed class WizardModel
{
    public string Name { get; set; } = "";
    public TreasureQuestDifficulty Difficulty { get; set; } = TreasureQuestDifficulty.Early;
    public int XpCost { get; set; }
    public bool AllowWarrior { get; set; }
    public bool AllowArcher { get; set; }
    public bool AllowMage { get; set; }
    public bool AllowThief { get; set; }
    public string? Description { get; set; }
    public string? QuestCardText { get; set; }
    public string? RewardCardText { get; set; }
    public string? RewardNote { get; set; }
    public string? Notes { get; set; }
    public List<int> SkillRewardIds { get; } = new();
    public List<(int ItemId, int Qty)> ItemRewards { get; } = new();
    public List<(int SpellId, int Qty)> SpellRewards { get; } = new();
    public int? GameXpOverride { get; set; }
    public int? PerKingdomLimit { get; set; }
}
```

**Save sequence:**
```csharp
private async Task SaveAsync()
{
    if (string.IsNullOrWhiteSpace(model.Name)) { error = "Název je povinný."; return; }
    if (model.XpCost < 0) { error = "XP cena nesmí být záporná."; return; }

    error = null; isSaving = true;
    try
    {
        progressLabel = "Vytvářím quest…";
        var detail = await Api.PostAsync<CreatePersonalQuestDto, PersonalQuestDetailDto>(
            "/api/personal-quests",
            new CreatePersonalQuestDto(
                model.Name, model.Difficulty, model.Description,
                model.AllowWarrior, model.AllowArcher, model.AllowMage, model.AllowThief,
                model.QuestCardText, model.RewardCardText, model.RewardNote, model.Notes,
                model.XpCost));

        progressLabel = "Přidávám odměny…";
        foreach (var id in model.SkillRewardIds)
            await Api.PostAsync<AddSkillRewardDto, object?>(
                $"/api/personal-quests/{detail.Id}/skill-rewards", new AddSkillRewardDto(id));
        foreach (var (itemId, qty) in model.ItemRewards)
            await Api.PostAsync<AddItemRewardDto, object?>(
                $"/api/personal-quests/{detail.Id}/item-rewards", new AddItemRewardDto(itemId, qty));
        foreach (var (spellId, qty) in model.SpellRewards)
            await Api.PostAsync<AddSpellRewardDto, object?>(
                $"/api/personal-quests/{detail.Id}/spell-rewards", new AddSpellRewardDto(spellId, qty));

        progressLabel = "Přidávám do hry…";
        await Api.PostAsync<CreateGamePersonalQuestDto, object?>(
            "/api/personal-quests/game-link",
            new CreateGamePersonalQuestDto(GameId, detail.Id, model.GameXpOverride, model.PerKingdomLimit));

        progressLabel = "Hotovo";
        await Saved.InvokeAsync();
        await VisibleChanged.InvokeAsync(false);
        ResetModel();
    }
    catch (Exception ex) { error = $"{progressLabel} — {ex.Message}"; }
    finally { isSaving = false; }
}

private void OnSkillCreated(SkillDto s)
{
    allSkills.Add(s);
    if (!model.SkillRewardIds.Contains(s.Id)) model.SkillRewardIds.Add(s.Id);
    skillCreatorVisible = false;
}
// similar for Item, Spell
```

**Reward-section UI (one per type):**
- combobox of existing (filtered to non-learnable for spells, exclude already-added)
- "Přidat" button that pushes to `model.SkillRewardIds` / `ItemRewards` / `SpellRewards`
- "Vytvořit novou" button that flips the inline-creator `visible` flag
- badges below for already-added rewards with `×` remove button
- for Item + scroll-Spell: quantity spinner next to combobox

Match the styling of the rewards section in `PersonalQuestList.razor` (Phase 1) so the two popups feel consistent.

**Verification:** `dotnet build` clean. No integration tests — UI only.

**Commit:**
```bash
git add src/OvcinaHra.Client/Pages/PersonalQuests/QuestWizard.razor
git commit -m "feat(quest-wizard): QuestWizard component with smart form + sequential save [v0.10.1]"
```

---

## Task 3 — Launch button + mount + version bump

One implementer dispatch. One commit.

**Files:**
- Modify: `src/OvcinaHra.Client/Pages/PersonalQuests/PersonalQuestList.razor` — in the game-view toolbar next to the existing "+ Nový" button, add:

  ```razor
  @if (!Catalog && GameContext.SelectedGameId is int wizardGameId)
  {
      <button class="btn btn-outline-primary ms-2" @onclick="() => wizardVisible = true">
          <i class="bi bi-magic me-1"></i>Vytvořit quest pro hru
      </button>
  }
  ```

  Then mount the wizard component anywhere in the page (outside the existing popup):

  ```razor
  @if (GameContext.SelectedGameId is int wizardMountGameId)
  {
      <QuestWizard @bind-Visible="@wizardVisible"
                   GameId="@wizardMountGameId"
                   CatalogXpFallback="0"
                   Saved="@LoadAsync" />
  }
  ```

  Add to `@code`:
  ```csharp
  private bool wizardVisible;
  ```

  `LoadAsync` already exists (it's the method that refreshes the grid). Hook it to `Saved`.

- Modify: `src/OvcinaHra.Client/OvcinaHra.Client.csproj:10` — bump `0.10.0` to `0.10.1`.

**Verification:** Full `dotnet build` green. `dotnet test tests/OvcinaHra.Api.Tests/OvcinaHra.Api.Tests.csproj --nologo` still 260 passing (no test changes here, but confirm no regression).

**Commit:**
```bash
git add src/OvcinaHra.Client/Pages/PersonalQuests/PersonalQuestList.razor \
        src/OvcinaHra.Client/OvcinaHra.Client.csproj
git commit -m "feat(quest-wizard): launch button + version bump to 0.10.1 [v0.10.1]"
```

---

## Post-tasks

1. Push: `git push -u origin feat/personal-quest-wizard`
2. Open PR via `gh pr create` — title `feat(personal-quest-wizard): guided create flow with inline reward creators [v0.10.1]`
3. Wait for CI (`build`, `build-and-test` required by branch protection).
4. If Copilot leaves review comments: fix each one with a fixup commit, push, re-wait for CI.
5. Squash-merge, delete branch.
6. Watch the auto-deploy: main → CD pipeline → Azure Container App. Verify `/api/version` returns `0.10.1` once deploy completes.

## Out of scope (explicit)

- Editing existing quests via wizard — stays in the popup.
- Draft save / resume.
- Batch create.
- New server endpoints.
- Integration tests for the UI (covered by Phase 1's API tests + manual smoke after deploy).
