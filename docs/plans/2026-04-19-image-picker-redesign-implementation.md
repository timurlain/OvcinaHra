# ImagePicker Redesign Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Replace the ugly native-`<input type=file>` mode of `ImagePicker` with a single click-to-upload box that takes a caller-specified aspect ratio and width.

**Architecture:** One Blazor component file is enhanced in place (no new component). Markup reduces to a single "box" mode — the previous `CardMode` parameter is deleted and the default "Choose File" mode is deleted. All 6 existing callers are updated to pass `AspectRatio` and remove any `CardMode="true"`.

**Tech Stack:** Blazor WASM, scoped CSS (`.razor.css`), Bootstrap classes already present in the project, existing `ApiClient` and image upload endpoints.

**Design doc:** `docs/plans/2026-04-19-image-picker-redesign-design.md` — read this first.

**Project conventions reminders:**
- Code in English, UI text in Czech with diacritics
- Hrdina/Hrdinové where any player reference is made (not relevant here — no player text)
- "Dobrodruh" is a skill category label, not for this component
- Commit after every task; include `[v0.5.1]` (or whatever the next patch is) in commit messages per project convention
- Run the client build after each code change: `dotnet build src/OvcinaHra.Client/OvcinaHra.Client.csproj` — 0 errors, warnings OK if pre-existing

---

## Task 1 — Refactor `ImagePicker.razor` to single box mode

**Files:**
- Modify: `src/OvcinaHra.Client/Components/ImagePicker.razor`
- Modify: `src/OvcinaHra.Client/Components/ImagePicker.razor.css`

**Step 1: Replace `ImagePicker.razor` contents** with the single-mode markup.

```razor
@using Microsoft.AspNetCore.Components.Forms

<div class="image-picker-wrap">
    <label class="image-picker-box @(currentUrl is null ? "is-empty" : "is-filled") @(isWorking ? "is-working" : "")"
           style="width: @Width; aspect-ratio: @parsedAspect;">
        @if (currentUrl is not null)
        {
            <img src="@currentUrl" alt="@Alt" class="image-picker-img" />
            <button type="button" class="image-picker-delete"
                    @onclick="DeleteImage" @onclick:stopPropagation="true"
                    disabled="@isWorking" title="Odebrat obrázek">&#10005;</button>
        }
        else
        {
            <div class="image-picker-empty">
                <span class="image-picker-cross">&#10005;</span>
                <span class="image-picker-caption">Nahrát obrázek</span>
            </div>
        }

        @if (isWorking)
        {
            <div class="image-picker-spinner">
                <span class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span>
            </div>
        }

        <InputFile OnChange="HandleFileSelected"
                   accept=".jpg,.jpeg,.png,.webp"
                   disabled="@isWorking"
                   class="image-picker-input" />
    </label>

    @if (errorMessage is not null)
    {
        <div class="text-danger small mt-1">@errorMessage</div>
    }
</div>

@code {
    [Inject] private ApiClient Api { get; set; } = null!;

    [Parameter, EditorRequired] public string EntityType { get; set; } = "";
    [Parameter, EditorRequired] public int EntityId { get; set; }
    [Parameter] public string? Field { get; set; }
    [Parameter] public string Alt { get; set; } = "Obrázek";

    [Parameter] public string AspectRatio { get; set; } = "1:1";
    [Parameter] public string Width { get; set; } = "200px";

    private string? currentUrl;
    private string? errorMessage;
    private bool isWorking;

    private string parsedAspect = "1 / 1";

    protected override void OnParametersSet()
    {
        parsedAspect = ParseAspectRatio(AspectRatio);
    }

    protected override async Task OnParametersSetAsync()
    {
        if (EntityId > 0)
            await LoadImageUrl();
    }

    private static string ParseAspectRatio(string ratio)
    {
        var parts = ratio?.Split(':', 2) ?? [];
        if (parts.Length == 2
            && int.TryParse(parts[0], out var w) && w > 0
            && int.TryParse(parts[1], out var h) && h > 0)
        {
            return $"{w} / {h}";
        }
        Console.WriteLine($"[ImagePicker] invalid AspectRatio '{ratio}', falling back to 1:1.");
        return "1 / 1";
    }

    private async Task LoadImageUrl()
    {
        try
        {
            var urls = await Api.GetAsync<ImageUrlsDto>($"/api/images/{EntityType}/{EntityId}");
            currentUrl = Field == "placement" ? urls?.PlacementUrl : urls?.ImageUrl;
        }
        catch { currentUrl = null; }
    }

    private async Task HandleFileSelected(InputFileChangeEventArgs e)
    {
        errorMessage = null;
        isWorking = true;

        try
        {
            var file = e.File;
            if (file.Size > 5 * 1024 * 1024)
            {
                errorMessage = "Soubor je příliš velký (max 5 MB).";
                return;
            }

            var query = Field is not null ? $"?field={Field}" : "";
            using var content = new MultipartFormDataContent();
            using var stream = file.OpenReadStream(maxAllowedSize: 5 * 1024 * 1024);
            var fileContent = new StreamContent(stream);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(file.ContentType);
            content.Add(fileContent, "file", file.Name);

            var result = await Api.PostMultipartAsync<ImageUploadResult>(
                $"/api/images/{EntityType}/{EntityId}{query}", content);
            if (result is not null)
                currentUrl = result.Url;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
        }
        finally
        {
            isWorking = false;
        }
    }

    private async Task DeleteImage()
    {
        isWorking = true;
        try
        {
            var query = Field is not null ? $"?field={Field}" : "";
            await Api.DeleteAsync($"/api/images/{EntityType}/{EntityId}{query}");
            currentUrl = null;
        }
        catch (Exception ex) { errorMessage = ex.Message; }
        finally { isWorking = false; }
    }
}
```

**Step 2: Replace `ImagePicker.razor.css`** with scoped styles for the box.

```css
.image-picker-wrap {
    display: inline-block;
}

.image-picker-box {
    position: relative;
    display: block;
    overflow: hidden;
    border-radius: 4px;
    cursor: pointer;
    transition: background-color 0.15s ease-in-out;
    background-color: #fff;
}

.image-picker-box.is-empty {
    border: 2px dashed #adb5bd;
    background-color: #fafafa;
}

.image-picker-box.is-empty:hover {
    background-color: #f0f0f0;
}

.image-picker-box.is-working {
    cursor: wait;
}

.image-picker-img {
    display: block;
    width: 100%;
    height: 100%;
    object-fit: cover;
}

.image-picker-empty {
    position: absolute;
    inset: 0;
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    gap: 0.35rem;
    color: #6c757d;
    pointer-events: none;
}

.image-picker-cross {
    color: #dc3545;
    font-size: 2.5em;
    line-height: 1;
    font-weight: 700;
}

.image-picker-caption {
    font-size: 0.85rem;
}

.image-picker-delete {
    position: absolute;
    top: 0.3rem;
    right: 0.3rem;
    width: 2rem;
    height: 2rem;
    line-height: 1;
    padding: 0;
    border: none;
    border-radius: 4px;
    background-color: #dc3545;
    color: #fff;
    font-size: 1rem;
    cursor: pointer;
    z-index: 2;
}

.image-picker-delete:hover {
    background-color: #c82333;
}

.image-picker-delete:disabled {
    opacity: 0.5;
    cursor: not-allowed;
}

.image-picker-spinner {
    position: absolute;
    inset: 0;
    display: flex;
    align-items: center;
    justify-content: center;
    background-color: rgba(255, 255, 255, 0.6);
    pointer-events: none;
    z-index: 1;
}

.image-picker-input {
    position: absolute;
    inset: 0;
    opacity: 0;
    cursor: pointer;
}
```

**Step 3: Build and verify**

Run: `dotnet build src/OvcinaHra.Client/OvcinaHra.Client.csproj`
Expected: 0 errors. Warnings are allowed if pre-existing (e.g. NU1903 for `System.Security.Cryptography.Xml` — already-known package issue not in scope).

**Step 4: Commit**

```bash
git add src/OvcinaHra.Client/Components/ImagePicker.razor src/OvcinaHra.Client/Components/ImagePicker.razor.css
git commit -m "feat(imagepicker): single box mode with AspectRatio + Width [v0.5.1]"
```

---

## Task 2 — Migrate all 6 callers

**Files to modify** (one commit per file; total 5 files):

- `src/OvcinaHra.Client/Components/LocationEditPopup.razor` (2 occurrences at lines 106, 109)
- `src/OvcinaHra.Client/Pages/Items/ItemList.razor` (line 189)
- `src/OvcinaHra.Client/Pages/Monsters/MonsterList.razor` (line 111)
- `src/OvcinaHra.Client/Pages/Npcs/NpcList.razor` (line 107 — also has `CardMode="true"` to remove)
- `src/OvcinaHra.Client/Pages/SecretStashes/SecretStashList.razor` (around line 76; may span multiple lines)

For each caller, apply these rules:

1. Remove any `CardMode="true"` attribute (the parameter no longer exists — leaving it in will be a compile error).
2. Add `AspectRatio="<ratio>"` based on domain context:
   - **Locations illustration** (`Alt="Ilustrace lokace"`) → `AspectRatio="3:2"` (landscape)
   - **Locations placement photo** (`Field="placement"`, `Alt="Foto umístění"`) → `AspectRatio="3:2"` (real-world photo, landscape)
   - **Items** (`Alt="Obrázek předmětu"`) → `AspectRatio="2:3"` (Magic the Gathering card proportions — portrait)
   - **Monsters** (`Alt="Obrázek příšery"`) → `AspectRatio="1:1"`
   - **NPCs** (`Alt="Obrázek NPC"`) → `AspectRatio="2:3"` (portrait)
   - **Secret Stashes** → inspect the surrounding dialog to pick a reasonable ratio; `3:2` if unsure.
3. Leave `Width` unset (default `200px`) unless the surrounding layout clearly needs a different value. If a caller is in a wide popup column where `300px` looks better, set `Width="300px"`.

### Task 2.1 — LocationEditPopup.razor

**Before (line 106):**
```razor
<ImagePicker EntityType="locations" EntityId="editingId.Value" Alt="Ilustrace lokace" />
```
**After:**
```razor
<ImagePicker EntityType="locations" EntityId="editingId.Value" Alt="Ilustrace lokace" AspectRatio="3:2" />
```

**Before (line 109):**
```razor
<ImagePicker EntityType="locations" EntityId="editingId.Value" Field="placement" Alt="Foto umístění" />
```
**After:**
```razor
<ImagePicker EntityType="locations" EntityId="editingId.Value" Field="placement" Alt="Foto umístění" AspectRatio="3:2" />
```

Build + commit:
```bash
dotnet build src/OvcinaHra.Client/OvcinaHra.Client.csproj
git add src/OvcinaHra.Client/Components/LocationEditPopup.razor
git commit -m "feat(imagepicker): set 3:2 aspect on location popup images [v0.5.1]"
```

### Task 2.2 — Items/ItemList.razor

**Before:**
```razor
<ImagePicker EntityType="items" EntityId="editingId.Value" Alt="Obrázek předmětu" />
```
**After:**
```razor
<ImagePicker EntityType="items" EntityId="editingId.Value" Alt="Obrázek předmětu" AspectRatio="2:3" />
```

Commit:
```bash
dotnet build src/OvcinaHra.Client/OvcinaHra.Client.csproj
git add src/OvcinaHra.Client/Pages/Items/ItemList.razor
git commit -m "feat(imagepicker): set 2:3 aspect on item dialog image (MtG-card proportions) [v0.5.1]"
```

### Task 2.3 — Monsters/MonsterList.razor

**Before:**
```razor
<ImagePicker EntityType="monsters" EntityId="editingId.Value" Alt="Obrázek příšery" />
```
**After:**
```razor
<ImagePicker EntityType="monsters" EntityId="editingId.Value" Alt="Obrázek příšery" AspectRatio="1:1" />
```

Commit:
```bash
dotnet build src/OvcinaHra.Client/OvcinaHra.Client.csproj
git add src/OvcinaHra.Client/Pages/Monsters/MonsterList.razor
git commit -m "feat(imagepicker): set 1:1 aspect on monster dialog image [v0.5.1]"
```

### Task 2.4 — Npcs/NpcList.razor (removes `CardMode="true"` too)

**Before:**
```razor
<ImagePicker EntityType="npcs" EntityId="editingId.Value" Alt="Obrázek NPC" CardMode="true" />
```
**After:**
```razor
<ImagePicker EntityType="npcs" EntityId="editingId.Value" Alt="Obrázek NPC" AspectRatio="2:3" />
```

Commit:
```bash
dotnet build src/OvcinaHra.Client/OvcinaHra.Client.csproj
git add src/OvcinaHra.Client/Pages/Npcs/NpcList.razor
git commit -m "feat(imagepicker): drop CardMode, set 2:3 portrait on NPC dialog [v0.5.1]"
```

### Task 2.5 — SecretStashes/SecretStashList.razor

**Before (around line 76 — read the file to confirm exact lines; may span multiple lines):**
```razor
<ImagePicker EntityType="secretstashes" EntityId="editingId.Value"
             ... />
```
**After:** add `AspectRatio="3:2"` to the attribute list. If there's a `CardMode="true"`, remove it.

Commit:
```bash
dotnet build src/OvcinaHra.Client/OvcinaHra.Client.csproj
git add src/OvcinaHra.Client/Pages/SecretStashes/SecretStashList.razor
git commit -m "feat(imagepicker): set 3:2 aspect on secret-stash dialog image [v0.5.1]"
```

---

## Task 3 — Wire the Skills page image upload

**File:** `src/OvcinaHra.Client/Pages/Skills/Skills.razor`

This page currently has a stub placeholder for the image field — a `<div>Obrázek (nahrát později)</div>` TODO left from Phase 6 of the skills-domain feature. Now that the `/api/images/skills/{id}` endpoint is in place (Phase 9 of skills-domain referenced it), replace the stub with a real `ImagePicker`.

**Step 1:** Open the file and find the TODO block. It will look approximately like:

```razor
<!-- TODO: wire ImagePicker once /api/skills/{id}/image endpoint exists -->
<div>Obrázek (nahrát později)</div>
```

**Step 2:** Replace with:

```razor
<ImagePicker EntityType="skills" EntityId="editingId.Value" Alt="Obrázek dovednosti" AspectRatio="1:1" />
```

**Step 3:** Verify the image endpoint actually exists by checking `/api/images/skills/{id}` — if it doesn't yet, this task becomes "add the endpoint" (but that's out of scope for this refactor; confirm or skip).

If the endpoint doesn't exist in `src/OvcinaHra.Api/Endpoints/ImageEndpoints.cs`, leave the stub in place and note it in the final report so the user knows an API endpoint is still missing.

**Step 4:** Build + commit if the upload is wired:
```bash
dotnet build src/OvcinaHra.Client/OvcinaHra.Client.csproj
git add src/OvcinaHra.Client/Pages/Skills/Skills.razor
git commit -m "feat(imagepicker): enable skill image upload [v0.5.1]"
```

---

## Task 4 — Manual browser smoke test

**Scope:** confirm the UI is correct across all 6 caller pages.

For each page listed below, go through the empty → upload → replace → delete flow. Confirm:

- Empty state shows a red X + "Nahrát obrázek" in a correctly-proportioned box.
- Hover darkens the box subtly.
- Clicking opens the file picker.
- After upload the box shows the image with `object-fit: cover` — no distortion, the aspect ratio matches what the caller requested.
- Small red `×` delete button sits top-right; clicking it removes the image without triggering a re-upload.
- During upload a spinner overlays the box and the box dims.
- 5 MB oversize file triggers "Soubor je příliš velký" Czech error below the box.

### Pages to exercise

1. **Items** — `/items` → open an item → see the 1:1 square box.
2. **Monsters** — `/monsters` → edit a monster → 1:1 square.
3. **NPCs** — `/npcs` → edit an NPC → 2:3 portrait.
4. **Locations** — `/locations` or the location edit popup → edit a location → **two** image pickers (illustration 3:2 and placement 3:2).
5. **Secret Stashes** — open a stash → 3:2.
6. **Skills** — `/skills` → create/edit a skill → 1:1 (if Task 3 wired the upload).

If any page looks broken (wrong aspect, overflow, misaligned delete button), fix the CSS in Task 1's `.razor.css` and commit that fix separately:

```bash
git commit -m "fix(imagepicker): <specific issue fixed> [v0.5.1]"
```

---

## Completion checklist

- [ ] Task 1 committed — component + CSS
- [ ] Tasks 2.1–2.5 committed — all 5 caller files updated
- [ ] Task 3 — skills page either wired or deferred with a note
- [ ] Task 4 — manual smoke passed on all 6 pages
- [ ] `dotnet build` clean (0 errors) at solution level
- [ ] No references to `CardMode` in the codebase: `grep -rn "CardMode" src/OvcinaHra.Client --include="*.razor"` returns nothing
- [ ] Version bumped in `OvcinaHra.Client.csproj` before deploy (per project convention — `[v0.5.1]` or next appropriate patch)

## Notes for the implementer

- This is a UI-only change. No API changes, no DB changes, no migrations.
- Scoped CSS (`.razor.css`) means the new selectors only affect this component. No `!important`, no global overrides.
- `<InputFile>` is the built-in Blazor file input. The trick to remove its native "Choose File" chrome is `opacity: 0` + `position: absolute; inset: 0` on top of the box so the full box is clickable and the native UI is invisible.
- The click propagation on the delete button (`@onclick:stopPropagation="true"`) prevents the replace flow from firing at the same time — keep it.
- If the implementation plan says to add an AspectRatio that doesn't visually fit, log it in the final report. The user can tweak later — don't guess silently.
