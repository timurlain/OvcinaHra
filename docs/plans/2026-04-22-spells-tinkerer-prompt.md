# Tinkerer Prompt — Spell Domain + Import

**Agent:** `hra-ovcina-tinkerer`
**Date:** 2026-04-22
**Scope:** Add `Spell` as a first-class domain entity in hra.ovcina.cz, matching the Item/Skill pattern. Import 28 spells from the rulemaster catalog. **Expose spells via the public JSON API** so the rulemaster bot and other skills can query them at runtime — same access pattern as Items/Monsters/Quests today.

---

## Context

We want to track spells in the world DB the same way we track items, monsters, and skills. Rules live in rulemaster (MAG-005 scrolls, MAG-006 mage spells I-V, MAG-007 learning costs, MAG-012 spell types) but the DB has no `Spell` entity yet.

**Source data (already prepared, do NOT regenerate):**
`docs/imports/spells.md` — 28 spells in structured tables (level 0 scrolls + levels I-V). This is the seed source. Parse it; do not hand-type spells.

**Reference entities to mirror:**
- `src/OvcinaHra.Shared/Domain/Entities/Item.cs` + `GameItem.cs` — closest structural match (catalog + per-game join)
- `src/OvcinaHra.Shared/Domain/Entities/Skill.cs` — closest semantic match (learned by characters, has categories)

---

## Design (agreed with user — do not change without asking)

### `Spell` entity

```csharp
public class Spell
{
    public int Id { get; set; }
    public required string Name { get; set; }         // "Ohnivá střela"
    public int Level { get; set; }                     // 0-5 (0 = scroll)
    public int ManaCost { get; set; }                  // 0 for scrolls, 1-5 for mage spells
    public SpellSchool School { get; set; }
    public bool IsScroll { get; set; }                 // true = MAG-005 scroll (one-shot, usable by anyone)
    public bool IsReaction { get; set; }               // Magický štít, Zrcadlo, Poslední dech
    public bool IsLearnable { get; set; }              // true = purchasable at a building. Default: !IsScroll.
    public int MinMageLevel { get; set; }              // 0 for scrolls, 1-5 for mage spells
    public int? Price { get; set; }                    // Learning cost in groše (MAG-007); null for scrolls
    public required string Effect { get; set; }        // Mechanical card text (Czech, diacritics)
    public string? Description { get; set; }           // Flavour / lore (optional)
    public string? ImagePath { get; set; }

    public ICollection<GameSpell> GameSpells { get; set; } = [];
    public ICollection<SpellBuildingRequirement> BuildingRequirements { get; set; } = [];
}
```

### `GameSpell` join (mirror `GameItem`)

```csharp
public class GameSpell
{
    public int Id { get; set; }               // Surrogate key (see GameSkill pattern if it uses one)
    public int GameId { get; set; }
    public int SpellId { get; set; }
    public int? Price { get; set; }           // Per-game override of learning cost
    public bool IsFindable { get; set; }      // Can be found as scroll/loot in this game
    public string? AvailabilityNotes { get; set; }

    public Game Game { get; set; } = null!;
    public Spell Spell { get; set; } = null!;

    public ICollection<GameSpellBuildingRequirement> BuildingRequirements { get; set; } = [];
}
```

> **Check first**: look at whether `GameSkill` uses a surrogate `Id` or composite `(GameId, SkillId)` key. Match that convention exactly — `GameSpellBuildingRequirement` below relies on it.

### `SpellBuildingRequirement` — learning location (mirror `SkillBuildingRequirement`)

```csharp
public class SpellBuildingRequirement
{
    public int SpellId { get; set; }
    public int BuildingId { get; set; }       // Plain int FK to Building — no enum, no string lookup

    public Spell Spell { get; set; } = null!;
    public Building Building { get; set; } = null!;
}
```

Composite key `(SpellId, BuildingId)`. Per MAG-007, every learnable mage spell (I-V) should have a `SpellBuildingRequirement` row for each of: Obchodník, Mudrc, Knihovna buildings (look them up in `Building` seed data by name). Scrolls (IsLearnable=false) have no rows here.

### `GameSpellBuildingRequirement` — per-game override (mirror `GameSkillBuildingRequirement`)

```csharp
public class GameSpellBuildingRequirement
{
    public int GameSpellId { get; set; }
    public int BuildingId { get; set; }

    public GameSpell GameSpell { get; set; } = null!;
    public Building Building { get; set; } = null!;
}
```

### Acquisition model — how a character gets a spell

Three paths, each modelled separately so they can coexist:

| Path | Mechanism | Entity / flag |
|------|-----------|---------------|
| **Learned at a building** | Pay `Price` groše at Obchodník/Mudrc/Knihovna | `Spell.IsLearnable = true` + `SpellBuildingRequirement` row(s) |
| **Quest reward** | Granted on quest completion | *Future:* `PersonalQuestSpellReward(PersonalQuestId, SpellId)` — mirror `PersonalQuestSkillReward`. **Not in scope for this task — but design the entity so a future migration can add it without schema churn.** |
| **Skill / level-up grant** | Granted by a skill or automatic at mage level-up (MAG-007: "one free spell of the new level") | *Future:* `SkillSpellReward(SkillId, SpellId)` or character-logic on level-up. **Not in scope.** |
| **Scroll in the world** | Physical one-shot item found as loot/treasure | `Spell.IsScroll = true`, `IsLearnable = false`. Linkage to loot/treasure uses existing `MonsterLoot` / `TreasureItem` patterns — **Item** currently holds those FKs. Follow up separately whether scrolls need their own loot table or can reuse Item. **Do not refactor that in this task.** |

**`IsLearnable = false`** means "not for sale at a shop by default" — the spell exists solely as reward / scroll / quest drop. Current catalog: all scrolls are `IsLearnable = false`, all mage spells I-V are `IsLearnable = true`.

### `SpellSchool` enum

Include **all classical elements + the non-elemental categories**, even if some don't have spells in the current catalog — they're reserved for future content.

```csharp
public enum SpellSchool
{
    Fire,       // Oheň — Jiskra, Ohnivá střela, Ohnivá koule, Ohnivá bouře, Připrav plamen/výheň
    Frost,      // Mráz — Ledový šíp
    Water,      // Voda — reserved (no spells yet, but part of the elemental set)
    Earth,      // Země — reserved
    Wind,       // Vítr — reserved
    Poison,     // Jed — Jedový šíp
    Mental,     // Mentální — Omámení, Zrcadlo, Omdli, Zatemnění mysli
    Support,    // Buffy/heal, non-elemental — Léčivé dlaně, Magický štít, Magická pomoc, Dodej kuráž, Rychlost, Poslední dech, Požehnej zbraně
    Utility     // Terén/vyvolávání/přehazování — Bahno, Klika, Kamenný pes, Větrná zeď, Bažina, Staff-fu, Instinktivní magie
}
```

Reserved values (Water/Earth/Wind) must be included now — the rulemaster bot will surface the enum as metadata and we want the elemental set complete even before content fills in.

---

## Tasks

Follow the project's TDD + verification loop. Commit only when user explicitly approves (see `feedback_no_commit_without_approval`).

1. **Domain entities** — create the following in `src/OvcinaHra.Shared/Domain/` following the layout conventions (Entities/ and Enums/):
   - `Spell.cs` (Entities)
   - `GameSpell.cs` (Entities)
   - `SpellBuildingRequirement.cs` (Entities)
   - `GameSpellBuildingRequirement.cs` (Entities)
   - `SpellSchool.cs` (Enums)

2. **EF Core configuration** — wire all four entities and the composite keys into `AppDbContext`:
   - `Spell` — PK `Id`, unique index on `Name`
   - `GameSpell` — match whatever `GameSkill` does (surrogate Id vs. composite key)
   - `SpellBuildingRequirement` — composite PK `(SpellId, BuildingId)`
   - `GameSpellBuildingRequirement` — composite PK `(GameSpellId, BuildingId)`
   - Match how `Skill` / `GameSkill` / `SkillBuildingRequirement` / `GameSkillBuildingRequirement` are configured.
   - Single migration named `AddSpells`.

3. **Seed loader** — write a parser that reads `docs/imports/spells.md`, parses the six per-level tables, and upserts into `Spell`. Follow whichever seed pattern the project uses (check how Items/Skills are seeded — if it's a `DataSeeder` service, extend it; if it's migrations + raw SQL, do that).
   - **Idempotent by Name.**
   - Rule applied by the loader: `IsLearnable = !IsScroll`.
   - After inserting spells, seed `SpellBuildingRequirement` by looking up the three buildings (Obchodník, Mudrc, Knihovna) in `Building` by name (or whatever seed key they use) and linking every learnable spell to every match. If a building type isn't in the seed data yet, **log a warning and continue** — don't block; we can re-seed later.

4. **DTOs + public API endpoints** — full CRUD + read endpoints so the rulemaster bot and other downstream skills can query spells the same way they query Items/Monsters/Quests today.

   Add `SpellDto`, `SpellListDto`, `SpellCreateDto`, `SpellUpdateDto`. Controller endpoints matching the Item/Skill controller surface:
   - `GET /api/spells` — list all (returns `SpellListDto[]`: Id, Name, Level, School, IsScroll, IsLearnable, ManaCost, MinMageLevel, Price)
   - `GET /api/spells/{id}` — full detail including `BuildingRequirements` (building IDs + names)
   - `GET /api/spells/by-game/{gameId}` — spells available in a game
   - `GET /api/spells/usable?mageLevel={n}&gameId={id}` — spells a mage of level N can cast (IsScroll=true OR MinMageLevel <= n). `gameId` optional.
   - `GET /api/spells/learnable?mageLevel={n}&buildingId={id}` — spells that can be **learned** at a specific building by a mage of level N (IsLearnable=true AND has SpellBuildingRequirement for that building AND MinMageLevel <= n)
   - `POST`/`PUT`/`DELETE /api/spells` — standard admin CRUD, behind the same auth policy as Item admin
   - `PUT /api/spells/{id}/buildings` — replace `SpellBuildingRequirement` set (accepts array of BuildingIds)
   - **Include spells in the existing `/api/search` FTS** (index Name, Effect, Description). The rulemaster bot uses `/api/search` as its primary lookup — spells must be discoverable there.
   - Auth: follow the pattern used for Items. If the bot needs unauthenticated read access, match whatever Items does (the rulemaster skill already carries a service token — `GET /api/items` works for it, so `GET /api/spells` should too with the same policy).

5. **Admin UI** — add spell management page to the admin catalog, mirroring the Items page. Respect existing patterns:
   - `feedback_ovcinahra_hrdina_copy` — user-facing Czech text says Hrdina/Hrdinové (not applicable here, but stay in the habit)
   - `feedback_ovcinagrid_layoutkey_bump` — if reusing an OvcinaGrid layout, bump LayoutKey
   - `feedback_ovcinahra_destructive_confirm` — delete goes through DxPopup confirmation
   - `feedback_ovcinahra_devexpress_runtime_attrs` — verify DxGridDataColumn attrs in browser console; runtime silently blanks the grid
   - `feedback_devana_radi_pattern` — tooltips via DxFlyout, not native title
   - `feedback_devana_1based_index` — level column displays 0-5 as written (scroll=0, I-V), no off-by-one

6. **Tests** — integration tests per `hra-ovcina-basher` conventions:
   - Spell CRUD round-trips (including `BuildingRequirements` collection)
   - Seed loader parses `spells.md` correctly: 28 rows, correct levels/schools/mana costs, `IsLearnable = !IsScroll`
   - `GET /api/spells/usable?mageLevel=2` → 6 scrolls + 4 level-I + 4 level-II = 14 rows
   - `GET /api/spells/usable?mageLevel=0` → 6 scrolls only
   - `GET /api/spells/learnable?mageLevel=3&buildingId={knihovna}` → 12 rows (I + II + III, all learnable and linked to building)
   - Spells appear in `GET /api/search?q=ohnivá` — at least Ohnivá střela, Ohnivá koule, Ohnivá bouře

7. **Version bump + commit message** — per `feedback_ovcinahra_version_in_commits` and `feedback_ovcinahra_version_in_deploys`, bump minor version (new entity type) and include `[vX.Y.Z]` in commit.

---

## Acceptance criteria

- [ ] `Spell` + `GameSpell` + `SpellBuildingRequirement` + `GameSpellBuildingRequirement` + `SpellSchool` enum in Domain, compiled clean
- [ ] Migration `AddSpells` applied locally, roundtrips on Postgres
- [ ] Seeder run produces exactly 28 spells from `docs/imports/spells.md`; `SpellBuildingRequirement` rows exist for every learnable spell × (Obchodník, Mudrc, Knihovna)
- [ ] All spell endpoints (`list`, `by-id`, `by-game`, `usable`, `learnable`, admin CRUD, buildings replace) return correct data; filtering math verified by tests
- [ ] `GET /api/search?q=...` returns spell matches alongside items/monsters/quests — **rulemaster bot must be able to find spells via search**
- [ ] Admin UI page shows grid, create/edit form (including building multi-select), confirmation on delete
- [ ] All tests pass, build clean, version bumped
- [ ] No commit without explicit user approval

---

## Out of scope (do NOT do)

- **Arcimágova hůl** — stays in `Item` table (it's an artifact, not a spell). User confirmed.
- **Koncentrace (MAG-003)** — not a spell, it's a mage special action. Belongs in character/combat logic, not Spell entity.
- **Mana regeneration (MAG-001)** — global combat rule, not a per-spell property.
- **Character → Spell learned-spells relationship** — out of scope for this task. When added later it'll be `CharacterSpell(CharacterId, SpellId, LearnedAt)` but don't do it now.
- **Resistance / weakness modeling on monsters** — the enum is ready for it (Dračí pancíř = Fire immunity) but don't wire monster resistances in this task.

---

## If you hit ambiguity

Stop and ask the user — do not invent. Especially:
- How existing entities are seeded (check before guessing — might be SQL migration, DataSeeder service, or JSON fixtures)
- Whether the search index is centrally wired (`/api/search` across Location/Item/Monster/Quest) — if yes, add Spell
- Whether there's an existing admin auth policy to reuse
