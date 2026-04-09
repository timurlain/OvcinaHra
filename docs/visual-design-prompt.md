# OvčinaHra — Visual Design Prompt

## Context

You are designing the visual theme and UI for **OvčinaHra**, a world-building and game content management app for **Ovčina** — a 30-year-running outdoor fantasy LARP for children (ages 6–15) and families in the Czech Republic. The game is set in Tolkien's Rhovanion and has four player kingdoms.

The app is built with **ASP.NET Core + Blazor WebAssembly** using **DevExpress Blazor** components. It is the sister app to [registrace.ovcina.cz](https://registrace.ovcina.cz) (registration & operations), sharing visual DNA but serving a different purpose.

**OvčinaHra manages the game world itself** — locations, monsters, quests, items, treasures, buildings, secret stashes, crafting recipes, and the interactive map. It is used by organizers to prepare game content at their desks, then carried into the forest on tablets during the live event.

This is not a generic CRUD admin panel. It should feel like an **explorer's workshop** — a cartographer's desk covered in field journals, bestiaries, and hand-drawn maps.

## Relationship to Registrace-Ovčina

Both apps are part of the ovcina.cz family and must feel like siblings:

**Shared:**
- Font stack (Merriweather, Inter, JetBrains Mono)
- Parchment texture language — warm cream/beige surfaces
- Kingdom colors (canon, identical across both apps)
- CSS variable naming conventions
- Overall warmth and hand-crafted feel

**Different:**
- **Registrace** leans warm brown/walnut — tavern table, invitation letter
- **OvčinaHra** leans **forest green** — ranger's field journal, cartographer's workshop
- Where registrace uses brown headers and saddle brown accents, OvčinaHra uses deep moss/pine greens
- The parchment is still the paper; the ink is green instead of brown

## Visual Identity

### Mood & Atmosphere

Organizers sit at a desk with maps spread out, field journals open, bestiaries bookmarked. Then they grab a tablet and walk into the forest to run the game. The aesthetic is:

- **Forest-green and earthy**, not cold or corporate
- **Field journal / naturalist's notebook** — organized knowledge with character
- **Cartographer's workshop** — maps are the centerpiece, everything relates to places
- **Warm but purposeful** — organizers are preparing and running a game, not browsing
- **30 years of tradition** — depth and richness, not a startup MVP

### Color Palette

Built on the same warm foundation as registrace, shifted toward forest green:

**Primary green accents (the shift):**
- Deep forest `#2D5016` — primary headers, nav bar, key accents
- Moss green `#4A7C34` — secondary text, labels, active states
- Fern `#6B9B37` — hover states, highlights
- Sage mist `#E8F0E4` — tinted surface backgrounds
- Pine bark `#3C4A2E` — dark mode elements, footer

**Shared warm foundation (from registrace):**
- Dark brown `#2C1810` — primary text (unchanged — text stays warm)
- Warm beige `#D4C4B0` — borders, grid lines, dividers
- Cream `#FFF8F0` — card/panel backgrounds
- Parchment `#F5EDE3` — section backgrounds
- Firebrick `#B22222` — danger accent, important numbers

**Burnt orange border** `#B26223` — card/panel border accent (shared with registrace)

**Kingdom colors** — canon, must be used consistently:

| Kingdom | Color | Hex | Used for |
|---------|-------|-----|----------|
| Aradhryand (Elves) | Green | `#2E7D32` | Kingdom badges, filters |
| Azanulinbar-Dum (Dwarves) | Red | `#C62828` | Kingdom badges, filters |
| Esgaroth (Lake-people) | Blue | `#1565C0` | Kingdom badges, filters |
| Nový Arnor (Mixed) | Yellow/Gold | `#F9A825` | Kingdom badges, filters |

> Note: The app's forest green (`#2D5016`) is darker and cooler than the Elven kingdom green (`#2E7D32`). They must remain visually distinct — the app green is "the workshop", the Elven green is "the kingdom".

### Typography

Identical to registrace for family consistency:

- **Headings:** Merriweather — serif, chronicle-like, book feel
- **Body text:** Inter — clean, readable, perfect Czech diacritics (č, ř, š, ž, ů)
- **Monospace / data:** JetBrains Mono — grids, coordinates, IDs
- **Avoid:** Generic system fonts, anything that looks like a Google Material template

### App Persona — Drozd (The Thrush)

**Drozd** is the app's guide and companion — inspired by the ancient thrush of Erebor who cracked snails on the grey stone on Durin's Day and showed the dwarves the secret door, then flew to Bard and revealed Smaug's weak spot.

In the Ovčina world, Drozd is a knowledge keeper — he listens, remembers, and brings crucial information to the right person at the right time. He speaks Czech.

**Visual appearance:** A large, dark thrush with spotted yellow breast (faithful to Tolkien's description). Illustrated in a style consistent with the hand-drawn game art — not cartoonish, not photorealistic. Warm ink-on-parchment feeling.

**UI appearances:**
- **Loading states:** Drozd knocking on stone (tap tap tap... ťuk ťuk ťuk...)
- **Process complete:** Drozd arrives with news, perches on the result
- **Help / guide tooltips:** Drozd perched beside text, whispering knowledge
- **Empty states:** Drozd waiting on a branch, looking around ("Zatím tu nic není...")
- **Error states:** Drozd with head tilted, confused ("Něco se pokazilo...")
- **Long-running tasks:** Drozd flying away, then returning with the result
- **First-time guidance:** Drozd walking the user through new features
- **Idle / ambient:** Small Drozd perched in the corner of the nav bar or footer

Drozd is helpful but not chatty. He appears when needed, not constantly. He has the quiet competence of a bird who has seen many seasons.

## Responsive Strategy — Desktop-First, Tablet-Second

Unlike registrace (mobile-first for parents on phones), OvčinaHra serves two primary contexts:

### Desktop / PC (Preparation Mode)
- Full sidebar navigation, rich data grids, bulk editing
- Map with all controls visible, detail panels beside it
- Split-pane layouts for list + detail views
- This is where organizers build the game world weeks before the event
- **Primary breakpoint: > 1024px**

### Tablet in Terrain (Game Mode)
- 10" tablet in landscape, held while standing in a forest
- Larger touch targets, good readability in outdoor light
- Quick lookup: "What monster is at this location? What's the quest text?"
- Simplified navigation — fewer options, bigger buttons
- Map as the primary entry point, tap location → see everything linked to it
- **Secondary breakpoint: 768–1024px**

### Phone (Not a Priority)
- Basic access should work but is not optimized
- Organizers won't be editing game content on a phone
- Simple responsive fallback, single column
- **Breakpoint: < 768px**

## Page-by-Page Design Direction

### Map Page (The Centerpiece)

This is the most important page — the cartographer's workbench:

- **Full-viewport MapLibre map** with minimal chrome
- Tile sources: Mapy.cz tourist/aerial/basic + OSM
- Color-coded location pins (by type or tag)
- **Name labels on markers** — visible at appropriate zoom levels
- Click location → slide-in detail panel (not a modal that hides the map)
- Filter controls: by type, tag, kingdom, quest association
- On tablet: map fills the screen, tap pin → bottom sheet with location info
- Subtle parchment border or frame around the map area
- **Background image opportunity:** parchment/map texture behind the map controls area

### Catalog Pages (Locations, Monsters, Items, Quests, Treasures, Buildings)

Each catalog is a page in a **bestiary or field guide**:

- **DxGrid** with warm-themed headers (forest green, not default purple)
- Alternating row colors using parchment tones (`#FFF8F0` / `#F5EDE3`)
- Click row → detail panel or page
- Inline quick-edit where practical
- Filters and search prominently placed
- Each entity type could have a subtle **icon or illustration** in the page header — a monster silhouette for the bestiary, a compass rose for locations, a sword for items
- **Background image opportunity:** subtle watermark illustrations behind the grid area (very low opacity)

### Detail / Edit Views

Filling in a **journal entry** about a creature or place:

- Form layout on parchment-toned card
- Section headers in Merriweather, forest green
- Image preview/upload area prominent (game assets are visual)
- Related entities shown as linked cards (location's monsters, quest's items)
- Map mini-view for locations (show pin in context)
- History/changelog as a subtle timeline

### Game Selector (Header)

- Dropdown or picker in the top bar — "Which game edition are you working on?"
- Shows game name, date, edition number
- Switching game reloads all catalog data
- Styled consistently with the forest green header bar

### Search Page

- Full-text search across all entity types
- Results grouped by type (Locations, Monsters, Quests...)
- Each result shows: name, type icon, brief excerpt, game edition
- Quick-jump to detail view

### Home / Overview

- Dashboard for the selected game edition
- Stats cards: X locations, Y monsters, Z quests, W items
- Recent changes feed
- Map preview (minimap of all locations)
- Drozd greeting with contextual tip
- **Background image opportunity:** hero section with a game event photo or the Rhovanion map, overlaid with the stats

## Background Images & Decorative Art

Background images give the app soul when chosen well:

**Where to use them:**
- Home page hero section — event photo or Rhovanion parchment map
- Login page — blurred forest scene or map
- Page headers — subtle watermark illustrations (very low opacity, 5-10%)
- Empty states — hand-drawn illustrations (Drozd on a branch, empty field journal)
- Map page controls area — parchment texture

**Where NOT to use them:**
- Behind data grids (hurts readability)
- Behind forms (distracting)
- Everywhere at once (overwhelming)

**Asset sources:**
- `games/.../MagicalDeckLokaceJsouCajk/assets/images/` — fantasy art (creatures, locations, battlefields, buildings)
- `games/.../MagicalDeckLokaceJsouCajk/assets/images/budovy/` — kingdom sanctuary illustrations
- `games/.../MagicalDeckLokaceJsouCajk/assets/icons/` — game icons (weapons, stars, quests)
- `games/2026 05 01 Balinova pozvánka/ifp6ekov8kpg1.jpeg` — Rhovanion parchment map
- `sources/documents/05 Ovcina/Podzim/domy_ovcina.jpg` — hand-drawn kingdom buildings
- `sources/documents/Karticky/` — legacy item, creature, and weapon art
- `sources/documents/Obrázky/Lokace/` — location reference images

**Do not use AI-generated images for public-facing content.** The charm is 30 years of real, handmade memories.

## DevExpress Theme Customization

The app uses DevExpress Blazor components. The default theme must be replaced:

### What to Override
- **DxGrid:** Header row → forest green background, cream text. Alternating rows → parchment tones. Selection → sage mist highlight.
- **DxPopup:** Border → burnt orange. Header → forest green or dark brown. Background → cream.
- **DxFormLayout:** Labels → moss green or saddle brown. Inputs → cream background, warm border.
- **DxButton (primary):** Forest green background, cream text. Hover → fern.
- **DxButton (secondary):** Parchment background, dark brown text, warm border.
- **DxComboBox, DxTextBox, DxSpinEdit:** Cream background, warm beige border, forest green focus ring.
- **DxTabs / DxPager:** Active tab → forest green underline or background.
- **Navbar:** Forest green (`#2D5016`) background, cream text, Drozd icon.

### Theme Approach
- Use DevExpress Bootstrap 5 theme as base
- Override via CSS custom properties and targeted selectors
- Keep a single `ovcinahra-theme.css` for all DevExpress overrides
- Maintain the CSS variable system for easy tuning

## CSS Custom Properties

```css
:root {
  /* === OvčinaHra green shift === */
  --color-primary: #2D5016;
  --color-primary-light: #4A7C34;
  --color-primary-lighter: #6B9B37;
  --color-primary-surface: #E8F0E4;
  --color-primary-dark: #3C4A2E;

  /* === Shared warm foundation (from registrace) === */
  --color-text-primary: #2C1810;
  --color-text-secondary: #8B4513;
  --color-accent: #B22222;
  --color-border: #D4C4B0;
  --color-surface: #FFF8F0;
  --color-surface-alt: #F5EDE3;
  --color-header: #2D5016;          /* green, not brown */
  --color-header-text: #FFF8F0;
  --color-card-border: #B26223;

  /* === Kingdom colors (canon) === */
  --color-kingdom-elves: #2E7D32;
  --color-kingdom-dwarves: #C62828;
  --color-kingdom-lake: #1565C0;
  --color-kingdom-arnor: #F9A825;

  /* === Status colors === */
  --color-status-draft: #8B4513;
  --color-status-active: #2D5016;
  --color-status-archived: #9E9E9E;

  /* === Functional (green-tinted) === */
  --color-success: #2E7D32;
  --color-warning: #F9A825;
  --color-error: #C62828;
  --color-info: #1565C0;

  /* === Typography === */
  --font-heading: 'Merriweather', 'Georgia', serif;
  --font-body: 'Inter', 'Segoe UI', sans-serif;
  --font-mono: 'JetBrains Mono', 'Consolas', monospace;

  /* === Spacing & radius === */
  --radius-card: 8px;
  --radius-button: 6px;
  --shadow-card: 0 2px 8px rgba(45, 80, 22, 0.12);
}
```

## What to Avoid

- Generic Material Design or Bootstrap blue/gray defaults
- DevExpress Blazing Berry purple (must be fully replaced)
- Stock photography of any kind
- "Dark mode gaming" aesthetic — this is for families, not gamers
- Overly ornate fantasy borders that slow page load or hurt readability
- Cookie-cutter SaaS dashboard layouts without theming
- Treating this as a simple CRUD admin panel
- Making Drozd annoying — he's helpful, not Clippy

## What to Nail

- **The green shift** must be immediately noticeable but harmonious with the warm foundation
- **The map page** is the crown jewel — it must feel like opening a cartographer's atlas
- **Catalog pages** should feel like leafing through a field guide, not scrolling a spreadsheet
- **Drozd** should feel like a welcome companion, not a gimmick
- **Kingdom colors** must be instantly recognizable
- **Tablet readability** in outdoor light — good contrast, large enough text and targets
- **Real game art** gives the app its soul — use it where it works, don't force it everywhere
- **Desktop preparation → tablet execution** transition should feel natural
