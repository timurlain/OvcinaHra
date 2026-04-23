# Design Brief — Item detail `/items/{id}`

**Status:** v1 · 2026-04-23
**Target surface:** Claude Design (Ovčina org, design system inherited)
**Implementation target:** new `src/OvcinaHra.Client/Pages/Items/ItemDetail.razor`

## Why it matters

Items anchor multiple gameplay systems: crafting (ingredients + outputs), quest rewards, monster loot tables, treasure quest contents, per-game shop. The detail page needs to make these interconnections legible — organizers should see at a glance "this sword is crafted from iron + hilt, drops from 3 monsters, is a reward in 2 quests, sold in shop X."

## Current state

- No dedicated detail page
- `ItemList.razor` opens a popup edit form on row click
- Entity: `Item { Name, ItemType, Effect, PhysicalForm, IsCraftable, ClassRequirements (VO: 4 ints), IsUnique, IsLimited, ImagePath }`
- Relationships: GameItems (per-game pricing/stock) · CraftingIngredients (used IN recipes) · MonsterLoots · QuestRewards · TreasureItems
- CraftingRecipe has `OutputItemId` — recipes that PRODUCE this item are queried that way

## Prompt for Claude Design

```
Design the Item detail page at /items/{id:int} for OvčinaHra — an
organizer's single-item deep view. Must feel like an illuminated
shop ledger: real photograph, clear stats, and all the interconnections
(crafting · loot · rewards · shop) laid out plainly.

AUDIENCE & DEVICE
Organizers planning encounters, shop economies, quest rewards.
Desktop first, 10" tablet secondary. Czech only.

PAGE SKELETON

  Breadcrumb: Předměty / {Item Name}
  Title strip (sticky):
    [← zpět]  {Name}  (h1 Merriweather 900 2rem)
    Subline:  ItemType chip · PhysicalForm chip · UNIKÁT ribbon · LIMIT stripe
    Right actions: [Upravit] / [Smazat] (read)
                   [Zrušit] / [Uložit] (edit)

  Tab strip (sticky, default=Karta):
    Karta · Upravit · Crafting · Výskyt · Obchod

============================================================
TAB 1 — Karta (illuminated ledger)
============================================================

Two-column desktop (60fr 40fr):

  LEFT: Hero photo at 4:3, heavy parchment frame (2 px #c4b494 border,
  inset gold radial, green-tinted shadow). Caption below:
    "Zbraň · Karta"  (type · physical form)

  RIGHT: stat block — vertical list of rows:
    [icon] LABEL ............. value
  Using:
    bi-shield-shaded   Válečník      Lv 3     (muted when 0)
    bi-bullseye        Lučištník     Lv 0     (muted)
    bi-magic           Mág           Lv 0
    bi-incognito       Zloděj        Lv 0
    hairline
    bi-stars           Unikát        ANO  (or hidden if false)
    bi-boxes           Limit         ANO
    bi-hammer          Craftable     ANO
  Mono values, right-aligned.

BELOW both columns:
  h3 "Efekt" + paragraph (Effect field, Georgia serif, justified).
  h3 "Fyzická podoba" + muted paragraph (PhysicalForm).
  If Effect is empty: italic grey "Efekt zatím není popsán."

============================================================
TAB 2 — Upravit
============================================================

DxFormLayout in sections:

  Identita:    Název · Typ (DxComboBox 19 items) · Fyzická podoba
  Vizuál:      ImagePicker EntityType="items", AspectRatio="4:3",
               Width="360px"
  Vlastnosti:  IsCraftable · IsUnique · IsLimited (3 DxCheckBox)
  Požadavky:   4-col row of DxSpinEdit — Válečník / Lučištník / Mág /
               Zloděj, each 0-10. Preview below renders the 4 class
               pips as they'll appear on Karta.
  Obsah:       Effect (DxMemo Rows=4)

Footer fixed at bottom: Smazat (ghost-bordeaux, far-left, editing
only) · spacer · Zrušit (secondary) · Uložit (primary, disabled until
Name set).

============================================================
TAB 3 — Crafting (both sides of the recipe graph)
============================================================

Two panels side-by-side (or stacked on narrow):

  PANEL A — "Vyrábí se z" (recipes that PRODUCE this item):
    Header: "Vyrábí se z (N receptů)"  + [+ Nový recept] button
    List of CraftingRecipe cards, one per game:
      · Game name + edition badge
      · Left-to-right ingredient chain:
          [ ingredient tile 72×96 ]  [ × 2 ]  +  [ ingredient tile ]
          [ × 1 ]  +  [ ingredient tile ]  [ × 3 ]   →   [ THIS ITEM ]
        Each ingredient tile is a mini 3:4 portrait of the ingredient
        photo + item name (small). Arrow chevron separators.
      · Row below the chain:
          [ bi-buildings Požadovaná budova: Kovárna ]
          [ bi-tools Dovednost: Kovářství Lv 3 ]
          [ bi-geo-alt Lokace: Kovárna u řeky ]
      · Small muted "GameId: {id} · edit" link on card top-right

  PANEL B — "Používá se v" (recipes that CONSUME this item as ingredient):
    Header: "Používá se v (N receptech)"
    Compact list of rows:
      [ output tile 48×64 ]  [ output name ]  [ × N ]  [ game badge ]
    Click → navigates to /items/{outputItemId}?tab=crafting

Empty states per panel: "Tento předmět se zatím nikde nevyrábí." /
"Tento předmět se nikde nepoužívá."

============================================================
TAB 4 — Výskyt (drops, rewards, treasures)
============================================================

Three grouped sub-sections with header row + items:

  "Kořist u příšer" (MonsterLoot):
    Mini-cards: monster thumbnail 72×54 + monster name + "× N" quantity
    pill + game badge. Click → /monsters/{id}.

  "Odměny v questech" (QuestReward):
    Compact list: quest name + difficulty badge + "× N quantity" mono
    pill + game badge. Click → /quests/{id}.

  "V pokladech" (TreasureItem):
    Treasure name + treasure difficulty + "× N" + game badge. Click →
    /treasures/{id}.

Each section has its own empty-state note if no rows.

============================================================
TAB 5 — Obchod (per-game shop configuration)
============================================================

Table-like list of GameItem rows, one per Game:
  | Hra | Cena | Sklad | Prodává | Nalezitelný | Podmínka prodeje | Akce |

  · Cena: mono "15 g" (gold accent pill)
  · Sklad: "3 / 10" mono (current / max, if max set) or "—"
  · Prodává / Nalezitelný: small badge (forest-green ANO / muted NE)
  · Podmínka prodeje: italic Georgia (e.g. "jen hrdinové 5+", "tajný
    obchod")
  · Akce: icons — Upravit (bi-pencil) + Odebrat (bi-trash ghost-bordeaux)

Empty state: "Tento předmět není v žádné hře." + "+ Přidat do hry"
primary button.

============================================================
STATES TO RENDER (stack vertically)
============================================================

  1. Karta — example "Meč elfů ze Stínového hvozdu": hero photo 4:3,
     Válečník Lv 3 / others 0, UNIKÁT, effect paragraph "2 životy, +1
     proti Nemrtvým".
  2. Upravit — same item, form populated, 4-col requirement row.
  3. Crafting — Panel A shows 2 recipes (current game + prior game)
     each with 3 ingredients + forge/skill requirements. Panel B shows
     this sword is used in 0 other recipes (empty).
  4. Výskyt — 2 monsters drop it (×1 each), 1 quest reward (×1), 1
     treasure (×1).
  5. Obchod — 2 games in table: current game 15g/3of10 sold/findable,
     prior game 12g/0of10 not-sold/findable, with a sale condition
     "jen po questu Tajemství hvozdu".
  6. Karta empty state — new item, no photo, no effect; Drozd perch
     in the photo slot.

ANTI-PATTERNS
  · No kingdom colors on ItemType chips.
  · No emoji.
  · No cinematic 16:9 hero — locked 4:3.
  · No Drozd on populated tabs.
  · Ingredient chain arrows must be clear — don't reduce to plain
    "+" without directionality.
```

---

## Builder addendum — Item detail

TARGET FILES
- New page: `src/OvcinaHra.Client/Pages/Items/ItemDetail.razor` (route `/items/{id:int}`)
- Theme additions: `.oh-it-detail-*` + `.oh-class-pips` block in `ovcinahra-theme.css`
- New components:
  - `src/OvcinaHra.Client/Components/ItemClassPips.razor` — reusable 4-pip row (used on tiles + detail + quest rewards)
  - `src/OvcinaHra.Client/Components/CraftingRecipeCard.razor` — the ingredient-chain display (used on Panel A of Crafting tab, and later on CraftingRecipe list pages)
- Update `ItemList.razor` row click → `NavigationManager.NavigateTo($"/items/{id}")` (replace popup-open-on-click with detail-page-navigation)

DATA BINDINGS
- GET `/api/items/{id}` → `ItemDetailDto` (exists; verify it carries crafting + loot + rewards, extend if not)
- Crafting Panel A: GET `/api/crafting/recipes?outputItemId={id}` — may need new endpoint
- Crafting Panel B: GET `/api/crafting/recipes?ingredientItemId={id}` — may need new endpoint
- Výskyt: existing MonsterLoot / QuestReward / TreasureItem queries; may need aggregate endpoint `/api/items/{id}/occurrences`
- Obchod: existing GameItem CRUD endpoints

NOTES
- Class-pip icons must match between ItemList tiles, ItemDetail Karta, and future QuestReward renders. Extract to shared component day 1.
- Ingredient chain rendering is a common pattern — future `CraftingRecipeDetail` page will want it too. Keep `CraftingRecipeCard` general (inputs · outputs · requirements).
- `Crafting` tab is the richest. If it starts to feel heavy in the first port, ship Tab 1/2/4/5 and stub Tab 3 with "Coming soon" — note it in the PR.
- `Item.ClassRequirements` is a value object (owned type). Edit flat via form, rebuild VO on save.
