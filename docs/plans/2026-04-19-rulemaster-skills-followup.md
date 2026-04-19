# Rulemaster Bot Follow-up: Skills Domain

**Date:** 2026-04-19
**Status:** Handoff note (no code in OvcinaHra)

## Purpose

The skills (dovednosti) domain just shipped in OvcinaHra. The rulemaster bot
(lives in a separate repo, under `.claude/skills/rulemaster-ovcina/` or an
equivalent agent config) already consumes `/api/items`, `/api/buildings`, and
friends. It now needs to be taught about the new `/api/skills`,
`/api/games/{id}/skills`, and the extended `/api/crafting` endpoints so it
can answer rule questions about which skill a recipe requires and who
(Dobrodruh vs. class-specific Hrdina) is allowed to learn it.

## OpenAPI discovery

All new routes are auto-exposed through the built-in .NET 10 OpenAPI document
at `/openapi/v1.json` — no manual API-side work is needed to publish them.

## New / extended endpoints

| Method | Path | Purpose |
|--------|------|---------|
| GET    | `/api/skills`                               | List all skills in the global catalog |
| GET    | `/api/skills/{id}`                          | Get a single skill by id |
| POST   | `/api/skills`                               | Create a new skill (class-specific or Dobrodruh) |
| PUT    | `/api/skills/{id}`                          | Update skill fields and its required-buildings list |
| DELETE | `/api/skills/{id}`                          | Delete a skill (blocked with 409 if referenced) |
| GET    | `/api/games/{gameId}/skills`                | List skills active in a game with XP cost and optional level req |
| PUT    | `/api/games/{gameId}/skills/{skillId}`      | Upsert a skill into the game (XP cost, optional level requirement) |
| DELETE | `/api/games/{gameId}/skills/{skillId}`      | Remove a skill from the game (409 if any recipe in that game still requires it) |
| PUT    | `/api/crafting/{id}`                        | Update recipe scalars + `RequiredSkillIds` |

Also extended: `POST /api/crafting` now accepts `RequiredSkillIds` in its body.

## Key DTO shapes

Source of truth: `src/OvcinaHra.Shared/Dtos/SkillDtos.cs`,
`GameDtos.cs`, `CraftingDtos.cs`.

```ts
// SkillDto
{
  Id: int,
  Name: string,
  ClassRestriction: PlayerClass | null,   // null = Dobrodruh (any class)
  Effect: string | null,
  RequirementNotes: string | null,
  ImagePath: string | null,
  RequiredBuildingIds: int[]
}
```

```ts
// CreateSkillRequest / UpdateSkillRequest (identical shape)
{
  Name: string,
  ClassRestriction: PlayerClass | null,
  Effect: string | null,
  RequirementNotes: string | null,
  RequiredBuildingIds: int[]
}
```

```ts
// GameSkillDto
{
  GameId: int,
  SkillId: int,
  SkillName: string,
  ClassRestriction: PlayerClass | null,
  XpCost: int,
  LevelRequirement: int | null
}

// UpsertGameSkillRequest
{ XpCost: int, LevelRequirement: int | null }
```

```ts
// CraftingRecipeDetailDto — extended field
{
  // ...existing fields (Id, OutputItemId, Ingredients, BuildingRequirements, ...)
  RequiredSkillIds: int[]
}

// CreateCraftingRecipeDto / UpdateCraftingRecipeDto — new parameter
{
  // ...existing fields
  RequiredSkillIds: int[] | null        // optional; null = no skills required
}
```

## Terminology

- **Dovednost** = "skill".
- **Class skill** — `ClassRestriction` set to a `PlayerClass` value. Only
  characters of that class may learn it.
- **Adventurer skill** — `ClassRestriction = null`. Any character may learn
  it. The UI label is **"Dobrodruh"**.
- A character / player is called **"Hrdina"** (sg.) / **"Hrdinové"** (pl.)
  throughout user-facing copy. Rulemaster answers should follow the same
  convention.

## Action items — for whoever updates the rulemaster bot

- [ ] Locate the rulemaster prompt/config that lists OvcinaHra item
  endpoints (likely `.claude/skills/rulemaster-ovcina/` or
  `.claude/agents/rulemaster-ovcina.md` in whichever repo hosts the bot).
- [ ] Add the 9 new endpoint lines from the table above.
- [ ] Add the DTO shapes above if the existing config carries DTO blocks
  for other entities.
- [ ] Add a terminology note: **Dobrodruh** = `ClassRestriction = null`;
  **Hrdina** / **Hrdinové** = character / players.
- [ ] Smoke-test the bot after the update:
  - "list all class skills"
  - "jaké dovednosti vyžadují kovárnu?"
  - "add skill X to game Y"
  - "which recipes in game Y need a specific skill?"
