# Design Brief — CharacterList edit dialog

**Status:** v2 (Claude-Design-native) · 2026-04-22
**Target surface:** Claude Design (claude.ai/design) — Ovčina org, design system already set up per `docs/design/CLAUDE-DESIGN-SETUP.md`
**Implementation target:** `src/OvcinaHra.Client/Pages/Characters/CharacterList.razor` + `ovcinahra-theme.css`

## Why this dialog matters

Characters are the face of the game. Organizers maintain a roster of 50–200 hrdinové (player characters + NPCs). Currently the dialog is a plain 600 px DxPopup with a flat form and no portrait — it reads like a CRUD panel, not a ranger's character sheet.

## Current pain

- **No portrait slot** — biggest miss; `ImagePicker` component already exists, ready to wire
- Race is free text — no structure, no consistency across the roster
- Kingdom doesn't exist as a concept yet (to be introduced)
- Rodič combobox lists the character itself and shows no portrait preview
- Poznámky memo is only 3 rows — cramped for a LARP with 30 years of lore
- Smazat button sits orphaned far-left in the footer
- Empty state uses a generic Bootstrap people-icon, not Drozd
- Header is plain text with no visual anchor

## Data-model prerequisites (apply before UI port)

1. **Race enum** on `Character.Race`: `Human`, `Dwarf`, `Elf`, `Hobbit`, `Dunedain` (Czech labels: Člověk, Trpaslík, Elf, Hobit, Dúnadan)
2. **Kingdom lookup table:** `Kingdom { Id, Name, HexColor, BadgeImageUrl, Description }`. Seed with the four canon kingdoms.
3. `Character.KingdomId` FK, nullable.
4. DTO updates: `CharacterListDto`, `CharacterDetailDto`, `CreateCharacterDto`, `UpdateCharacterDto` gain `Race` + `KingdomId`.
5. Migration + endpoint pass-through.

## Prompt for Claude Design

Paste this into a new Claude Design project within the Ovčina org (design system is inherited automatically — no palette/fonts/Drozd/kingdoms boilerplate needed).

---

```
Design the edit dialog for a Character in OvčinaHra (organizer tool,
desktop-first with 10" tablet as secondary). This is the detail view
opened from the Characters list — both for creating a new character
and editing an existing one.

FIELDS (Czech labels, render verbatim)

  Identity section (top)
    Název postavy          text, required
    Rasa                   chip-picker: Člověk · Trpaslík · Elf · Hobit · Dúnadan
    Království (Kingdom)   lookup dropdown; each option renders the
                           kingdom badge (48×48) + name + hex swatch;
                           "(žádné)" is allowed
    Hráčská postava        toggle Ano/Ne (shows/hides the Player panel)

  Portrait (right side of Identity)
    Single image slot, aspect ratio 4:5, display ~240×300
    Drag-drop or click to upload · empty state shows a small thrush-and-
    feather outline with caption "Nahrát portrét"
    Hover on filled image → burnt-orange × in top-right to remove

  Player panel (visible only when Hráčská = Ano)
    Jméno hráče            text
    Příjmení hráče         text
    ID v registraci        number; if set, show a compact pill-link
                           "Otevřít v registraci" (external-arrow icon)
                           aligned to the right edge of the row

  Lineage section
    Rodič (Parent)         combobox of other characters — EXCLUDE self;
                           each option renders a 24×30 portrait thumb +
                           name + small race chip
    Rok narození           number input; to its right, a computed age
                           read-out in muted walnut text

  Notes section
    Poznámky               multi-line memo, min 6 rows, resizable;
                           placeholder: "Deník kronikáře — příběh postavy,
                           vztahy, zvyky, poznámky z her…"

FOOTER (right-aligned except Smazat)
  Smazat        ghost-bordeaux (outline only), far-left, visible only
                when editing an existing character
  Zrušit        secondary
  Uložit        primary; disabled until Název is filled

HEADER
  Edit mode:    tiny 24×24 thrush icon before the title "Upravit: {name}"
  Create mode:  tiny 24×24 quill-plus icon before "Nová postava"
  Right side:   close × only

STATES TO RENDER (stack vertically on faint parchment page bg)

  1. Create (empty form, portrait empty, Hráčská off, Smazat hidden,
     Uložit disabled)
  2. Edit populated — example data:
       Název: "Radovan Větrný"
       Rasa: Dúnadan
       Království: Nový Arnor
       Hráčská: Ano, Jméno: "Tomáš", Příjmení: "Novák",
         ID v registraci: 1247
       Rodič: "Finarfin Větrný" with 24×30 thumb
       Rok narození: 1985 → age read-out "41 let"
       Poznámky: two short paragraphs of sample ranger lore in Czech
  3. Validation error — Název missing, red bordeaux helper text under
     the field, Uložit disabled; no intrusive alert banner

BEHIND THE DIALOG
Show a sliver of the Characters list grid (behind a soft backdrop):
  columns [portrait 32×40] · Název · Kingdom badge 24×24 · Rasa chip ·
  Hráč · Hráčská

AUDIENCE
Game organizers, ages 30–60, working at a desk to prepare an event
(primary) or on a 10" tablet in the forest during the event (secondary).
Czech-native, no English labels anywhere in the UI.

DROZD FREQUENCY
Drozd must NOT appear in the populated edit state.
He may appear as a 48×48 perch in the validation-error state,
muted, with caption "Něco chybí."
```

---

## Builder addendum — CharacterList

TARGET FILES
- Razor: `src/OvcinaHra.Client/Pages/Characters/CharacterList.razor`
- Theme additions: `src/OvcinaHra.Client/wwwroot/css/ovcinahra-theme.css` (only if Claude Design introduces new tokens — add to `:root`)
- New component (optional): `src/OvcinaHra.Client/Components/KingdomPicker.razor` (DxComboBox wrapper that renders badge + name)
- Shared component changes: none

DATA-MODEL PREREQS
- Migration: `AddRaceEnumAndKingdomLookup`
  - Add `Race` enum column on `Characters` (defaults to null)
  - Create `Kingdoms` table with `Id`, `Name`, `HexColor`, `BadgeImageUrl`, `Description`
  - Add `KingdomId` FK nullable on `Characters`
  - Seed four canonical kingdoms (Esgaroth, Aradhryand, Azanulinbar, Nový Arnor)
- Entity: `OvcinaHra.Shared/Domain/Entities/Kingdom.cs`
- Configuration: `OvcinaHra.Api/Data/Configurations/KingdomConfiguration.cs`
- Endpoint: `GET /api/kingdoms` returning `KingdomDto[]`
- DTO updates: `CharacterListDto`, `CharacterDetailDto`, `CreateCharacterDto`, `UpdateCharacterDto` gain `Race` (enum) + `KingdomId` (int?)

FIELD BINDINGS (mockup → Razor)
- Název postavy → `name` via `DxTextBox @bind-Text="@name"`
- Rasa → `race` (enum) via chip picker component (new) or `DxComboBox` with enum data
- Království → `kingdomId` via `KingdomPicker` bound to `/api/kingdoms`
- Hráčská postava → `isPlayedCharacter` via `DxCheckBox`
- Jméno / Příjmení hráče → `playerFirstName` / `playerLastName` via `DxTextBox`
- ID v registraci → `externalPersonId` via `DxSpinEdit` (conditional render)
- Rodič → `parentCharacterId` via `DxComboBox` (filter out `editingId` from data source)
- Rok narození → `birthYear` via `DxSpinEdit`
- Poznámky → `notes` via `DxMemo Rows="6"`
- Portrait → `ImagePicker` with `EntityType="character"`, `EntityId="@editingId"`, `AspectRatio="4:5"`, `Width="240px"`

IMAGE SLOTS
- Character portrait: `AspectRatio="4:5"`, `Width="240px"`, `EntityType="character"`, no `Field` (uses default image slot)
- Kingdom badge: served from `KingdomDto.BadgeImageUrl`, rendered at 48×48 in the picker option and 24×24 in grid column

GRID COLUMN CHANGES
- Add: `Portrait` (32×40 thumb at start), `Kingdom` (badge 24×24 + name), `Rasa` (chip)
- Keep: `Název`, `Hráč`, `Hráčská`, `ID v registraci`
- Bump `LayoutKey`: `"grid-layout-characters"` → `"grid-layout-characters-v2"`

VALIDATION
- `Název postavy` required, non-empty after trim
- If `Hráčská = true` and `ExternalPersonId` is set, must match an existing registrace person (server-side only)
- `BirthYear` must be ≥ 1900 and ≤ current year + 1

NOTES TO THE BUILDER
- `ImagePicker` can only bind when `EntityId > 0`. In Create mode, render a placeholder ("Portrét bude možné nahrát po uložení") and enable the slot after the first Save.
- Inherit all verification-loop and MEMORY rules from `docs/design/BUILDER-BRIEF-TEMPLATE.md` — do not re-state them here.
