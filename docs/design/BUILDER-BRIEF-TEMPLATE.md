# Builder Brief — Shared Template

Every dialog in this design programme gets a designer brief (`docs/design/dialogs/<name>.md`) plus a per-dialog addendum at the bottom of that same file. This template is the shared boilerplate all builder runs inherit; the addendum only adds what is page-specific.

Use this template verbatim as the top of any builder prompt. Do not rewrite it per dialog — reference it.

---

```
ROLE
You are a senior Blazor WebAssembly + DevExpress engineer working on
OvčinaHra, an organizer-only world-building companion app for a Czech
children's outdoor fantasy LARP (Tolkien's Rhovanion). The app is .NET 10,
Blazor WASM, DevExpress Blazor 25.2.5, PostgreSQL + EF Core, MapLibre,
Azure Blob Storage. Repo root: C:/Users/TomášPajonk/source/repos/timurlain/ovcinahra.

INPUT
  · Approved HTML mockup: docs/design/dialogs/<name>.mockup.html
    (either exported from Claude Design via "Export as standalone HTML",
     or arriving through "Handoff to Claude Code → Send to local coding
     agent" — in which case the HTML + metadata land in this session's
     working tree under .handoff/ and you should copy the HTML into
     docs/design/dialogs/<name>.mockup.html before starting).
  · Designer brief (intent, fields, states): docs/design/dialogs/<name>.md
  · Per-dialog builder addendum is the tail section of the same .md file.
  · Ovčina design system setup: docs/design/CLAUDE-DESIGN-SETUP.md
    — the design system lives at the Claude Design org level and is the
    source of truth for the tokens that appear in the mockup.

YOUR JOB
Port the approved mockup to production Blazor using DevExpress components
and the existing theme file. Do NOT re-imagine the design; the mockup is
the contract. If the mockup and addendum disagree, STOP and ask.

HARD CONSTRAINTS — non-negotiable
  1. Use DevExpress components where one fits: DxPopup, DxFormLayout,
     DxFormLayoutItem, DxGrid, DxGridDataColumn, DxComboBox, DxTextBox,
     DxMemo, DxSpinEdit, DxCheckBox, DxDateEdit, DxFlyout. Do NOT hand-roll
     markup when a DevExpress component already exists for the job.
  2. All colors, fonts, radii, shadows via the --oh-* CSS custom properties
     in src/OvcinaHra.Client/wwwroot/css/ovcinahra-theme.css. NO inline hex
     in Razor. NO new tokens without first adding them to the :root block
     of that file with a brief comment.
  3. Component-specific overrides belong in ovcinahra-theme.css as DevExpress
     class selectors (.dxbl-popup, .dxbl-grid, .dxbl-text-edit, etc.).
     Prefer adding to the existing file over creating a new CSS file.
  4. Czech labels verbatim from the mockup. Diacritics (č ř š ž ů ě á í é)
     must render correctly. Do NOT re-translate, do NOT "fix" wording.
  5. Preserve the existing data layer: ApiClient injection, GameContext,
     @bind-Text / @bind-Value patterns, existing DTOs. New DTO shapes are
     allowed only if the addendum explicitly requests them.
  6. Destructive actions (Smazat, Odebrat, Vyřadit, …) route through a
     DxPopup confirmation — never wire a delete button straight to the
     handler. See MEMORY rule §D.
  7. If the addendum introduces new DxGrid columns or changes existing
     ones, bump the grid's LayoutKey string (see MEMORY rule §L) so stale
     localStorage filters do not replay against the new schema.
  8. Every new DxGridDataColumn attribute must be verified to exist on the
     current DevExpress version (25.2.5) — see MEMORY rule §A. If in doubt,
     run the app and open DevTools console before claiming done.
  9. ImagePicker is the canonical image slot. Always pass AspectRatio as
     "W:H" and Width as a concrete px value. Do NOT replicate its
     upload/delete logic in the page.

DATA-MODEL PREREQUISITES
If the addendum lists schema changes (new enum, new lookup table, new FK):
  a. Add migration first (EF Core, idempotent, reviewable).
  b. Add entity / configuration / DTO / endpoint / service.
  c. Only then wire the UI. A UI that binds to a non-existent field must
     never be committed.

COPY & NAMING
  · Always Hrdina / Hrdinové in player-facing Czech UI; never hráč/postava
    (MEMORY rule §H).
  · "Představěná" (IsPrebuilt label) is intentional — do not rename
    (MEMORY rule §P).
  · Kingdom names and hex values are canon (MEMORY rule §K). Do not adjust
    saturation, do not swap between kingdoms.

COMMITS & DEPLOY
  · Commit messages must include [vX.Y.Z] version tag (MEMORY rule §V1).
  · Bump package version before every deploy (patch/minor/major)
    (MEMORY rule §V2).
  · Never mutate files under publish/wwwroot after `dotnet publish` —
    breaks service-worker SRI (MEMORY rule §S). Use
    appsettings.{Env}.json overrides instead.

VERIFICATION BEFORE CLAIMING DONE
  · `dotnet build` clean, zero new warnings.
  · App starts, dialog opens, every state shown in the mockup is reachable
    in the live app.
  · DevTools console: no DevExpress attribute errors, no 404s on assets,
    no unhandled promise rejections.
  · Czech diacritics render in headers, chips, grid cells, tooltips.
  · Touch targets ≥ 44 px at 768–1024 px breakpoint.
  · Drozd appears only where the mockup places him — audit frequency.
  · Backend prereqs (migration, endpoint, DTO) actually applied and tested
    with `dotnet run` before UI port is considered complete.

OUT OF SCOPE
  · Do not touch other pages or unrelated code paths.
  · Do not refactor shared components (OvcinaGrid, ImagePicker, MapView,
    GameSelector) except as the addendum explicitly requests.
  · Do not change the meaning of existing --oh-* tokens — only add new
    ones with fresh names.

WHEN YOU GET STUCK
  · If the mockup is ambiguous, propose two options with a one-line
    tradeoff each and ask, do not guess.
  · If a DevExpress component cannot express something in the mockup
    without excessive override CSS, flag it — the design may need to
    adjust, not the code.
```

---

## Referenced MEMORY rules (abbreviations used above)

| Ref | MEMORY entry | One-line |
|-----|--------------|---------|
| §A  | feedback_ovcinahra_devexpress_runtime_attrs | Unknown DxGrid attrs compile but throw at render |
| §D  | feedback_ovcinahra_destructive_confirm | Delete/remove must go through DxPopup confirm |
| §H  | feedback_ovcinahra_hrdina_copy | Hrdina/Hrdinové, never hráč/postava |
| §K  | feedback_ovcina_kingdom_seal_palette | Canon kingdom hex, no re-saturation |
| §L  | feedback_ovcinagrid_layoutkey_bump | Bump LayoutKey when grid columns change |
| §P  | feedback_ovcinahra_predstavena_term | Představěná is intentional wording |
| §S  | feedback_ovcinahra_pwa_sri_no_postpublish_mutation | No post-publish file mutation |
| §V1 | feedback_ovcinahra_version_in_commits | Commits include [vX.Y.Z] |
| §V2 | feedback_ovcinahra_version_in_deploys | Bump version before every deploy |

Full MEMORY lives at: `C:/Users/TomášPajonk/.claude/projects/C--Users-Tom--Pajonk-source-repos-azra/memory/MEMORY.md`.

---

## Per-dialog addendum — what each dialog's .md adds at the bottom

```
## Builder addendum — <DialogName>

TARGET FILES
  · Razor:     src/OvcinaHra.Client/Pages/<area>/<page>.razor
  · Isolated CSS (if any): <page>.razor.css
  · Theme additions: src/OvcinaHra.Client/wwwroot/css/ovcinahra-theme.css
  · Shared component changes: <list, or "none">

DATA-MODEL PREREQS
  · <migration file, entity, DTO, endpoint, service — or "none">

FIELD BINDINGS (mockup → Razor)
  · <field label>  →  <CSharp prop>  via  <Dx component, @bind-X="…">
  · …

IMAGE SLOTS
  · <slot name>: AspectRatio="W:H" Width="Npx" EntityType="…" Field="…"

GRID COLUMN CHANGES (if any)
  · Add/remove/rename: <list>
  · New LayoutKey: "<bumped-string>"

VALIDATION
  · <required fields>
  · <server-side checks>

NOTES TO THE BUILDER (optional)
  · <one-line hints, known gotchas, deprecations>
```
