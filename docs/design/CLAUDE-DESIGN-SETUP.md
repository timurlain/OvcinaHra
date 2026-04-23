# Claude Design — OvčinaHra Design-System Setup

**One-time onboarding.** Every dialog brief written afterwards inherits this — tokens, fonts, kingdom canon, Drozd all come from here, not from the prompt.

**Prerequisites**

- Claude Pro / Max / Team / Enterprise account (Claude Design is not on the free tier)
- Access to this repo: `C:/Users/TomášPajonk/source/repos/timurlain/ovcinahra`

---

## Step 1 — Open Claude Design and create the OvčinaHra org

1. Go to https://claude.ai/design
2. Project picker (bottom-left) → click current org → **Create new organization** (or reuse an existing one dedicated to Ovčina work)
3. Name it `Ovčina` (shared with future sister-app work for registrace.ovcina.cz)
4. Complete the onboarding flow — it will ask you to upload source material in the next step.

## Step 2 — Upload source material

Upload in this order (most signal first). The docs warn linking whole monorepos can lag — link subdirectories.

### 2a. Codebase

Link or upload these subpaths only (not the whole repo):

- `src/OvcinaHra.Client/wwwroot/css/ovcinahra-theme.css` — **the `--oh-*` token file, critical**
- `src/OvcinaHra.Client/wwwroot/css/app.css`
- `src/OvcinaHra.Client/Components/` — existing shared components (`ImagePicker`, `OvcinaGrid`, `MapView`, `GameSelector`, `LocationEditPopup`)
- `src/OvcinaHra.Client/Layout/` — navbar and page shell

### 2b. Screenshots of the current app

Capture from https://hra.ovcina.cz and upload:

- Home page
- Any one list page (e.g., Characters)
- The current edit popup on that list page
- The Map page (crown jewel — signals the aesthetic)

These teach Claude what we have; the design system job is to improve, not invent from zero.

### 2c. Hand-drawn game art (the heart of the aesthetic)

Pull from Ovčina OneDrive (read-only):

- `games/.../MagicalDeckLokaceJsouCajk/assets/` — parchment card art
- `games/2026 05 01 Balinova pozvánka/ifp6ekov8kpg1.jpeg` — Rhovanion parchment map
- `sources/documents/05 Ovcina/Podzim/domy_ovcina.jpg` — hand-drawn kingdom buildings
- Any existing Drozd sketch (if we have one) — otherwise Claude will propose one, we refine

### 2d. Kingdom canon (paste into the onboarding chat)

```
Four kingdoms — canonical hex, never swap, never re-saturate:
  Esgaroth       #242F3D   Lake-town humans, slate blue
  Aradhryand     #243525   Mirkwood elves, deep green
  Azanulinbar    #8C2423   Erebor dwarves, bordeaux
  Nový Arnor     #504B25   Northern rangers, muted gold

Race enum (render as chips):
  Člověk · Trpaslík · Elf · Hobit · Dúnadan

Every dialog may have a Kingdom badge (48×48) next to the entity name.
```

### 2e. Drozd — the mascot

```
Drozd (thrush of Erebor) is our mascot.
Hand-drawn ink-on-parchment style, quiet, helpful, never chatty.
Appears ONLY in empty / loading / error / first-run states — never on populated screens.

Canonical sizes:
  120×120   dialog empty-state illustration
   48×48   compact inline (beside error toast, beside an empty grid row)
   24×24   header corner mark (edit-dialog titles, page headers)

Czech micro-copy library:
  empty    "Zatím tu nic není."                "Zatím tu nikdo nepřebývá."
  loading  "Drozd listuje kronikou…"           "Posel je na cestě…"
  error    "Něco se pokazilo. Zkuste to znovu." "Drozd ztratil stránku."
  first    "Tady začne kronika."
```

### 2f. LocationKind marker colors (canon, distinct from kingdom palette)

Locations on the map and in the Locations grid use their own marker palette, tied to `LocationKind`. These are NOT kingdom colors and must not be swapped for them.

```
LocationKind marker colors (canon):
  Town        #2c3e50   dark slate       — Město
  Village     #27ae60   fresh green      — Vesnice
  Magical     #8e44ad   violet           — Kouzelné
  Hobbit      #f39c12   amber            — Hobit
  Wilderness  #16a085   teal             — Pustina

Usage:
  · 10 px dot in the Locations grid (leftmost cue)
  · circular map pin on /map and /locations/{id} › Mapa tab
  · small chip in the LocationKind combobox, prefixed to the label
```

## Step 3 — Review the generated design system

After Claude generates the UI kit, verify each item. Anything wrong, tell Claude in the onboarding chat to fix it before saving.

- [ ] Primary color is forest green `#2D5016` (not default Material blue, not Blazing Berry purple)
- [ ] Surface is parchment cream `#FFF8F0`; alt surface `#F5EDE3`
- [ ] Card border accent is burnt orange `#B26223`
- [ ] Headings use Merriweather; body Inter; monospace JetBrains Mono
- [ ] Czech diacritics (č ř š ž ů ě á í é) render cleanly at every size
- [ ] Drozd is registered as a mascot / state-illustration asset with the four state variants
- [ ] Kingdom lookups stored as a component/pattern with canonical hex, so new dialogs can reference "Kingdom badge" by name
- [ ] Destructive-action button style exists as a ghost-bordeaux variant (outline `#8C2423`, transparent fill, hover fills bordeaux)
- [ ] A modal / dialog pattern exists matching DxPopup shape: forest-green header with cream text, parchment body, burnt-orange 1 px border, large corner radius, softer top-corner radii only

## Step 4 — Known limitations to work around

From Anthropic's docs:

- **Inline comments occasionally disappear before Claude reads them.** Workaround: paste the comment text into the chat instead.
- **Large codebases cause lag.** Link subdirectories only, never the repo root.
- **"Chat upstream error":** start a new chat tab inside the same project; the design system persists.
- **Compact layout save errors:** switch to full view before saving.

## Step 5 — After setup, every dialog brief becomes tiny

Once the design system is in place, dialog briefs drop the tokens/fonts/Drozd/kingdom block. They carry only:

- One-line context (what page / what task)
- Field list (label, type, span)
- Image slots with aspect ratios
- States to render (create, populated, error, …)
- Data-model prereqs
- Any page-specific copy

See `docs/design/dialogs/character-list.md` for the canonical short-form brief.

---

## Handoff path: Claude Design → Claude Code

When a mockup is approved in Claude Design, the native export `Export → Handoff to Claude Code → Send to local coding agent` drops the approved HTML plus metadata into a fresh Claude Code session here. That session picks up the builder brief (`docs/design/BUILDER-BRIEF-TEMPLATE.md` + the per-dialog addendum) and ports the mockup to DevExpress Blazor.

Alternative: `Export as standalone HTML` → save at `docs/design/dialogs/<name>.mockup.html` → tell Claude Code in this repo *"<name> mockup approved"*.
