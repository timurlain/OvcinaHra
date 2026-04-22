# Tinkerer Handback — Spell API Documentation

**Agent:** `hra-ovcina-tinkerer`
**Date:** 2026-04-22
**Depends on:** `docs/plans/2026-04-22-spells-tinkerer-prompt.md` (must be complete first)
**Deliverable:** One markdown file — see "Output" below. No code changes.

---

## Why this task

Once the Spell domain is shipped, the **rulemaster skill** (a Claude skill owned by the user) needs to learn how to query it. The rulemaster skill already has a block documenting Items/Monsters/Locations/Quests API access. We're adding Spells.

The rulemaster skill lives outside this repo (in the user's `~/.claude/skills/rulemaster-ovcina/`). You're not editing it — you're writing a **handback document** the user will copy into that skill file.

**Audience:** a future Claude reading the skill at session start. It needs concrete curl commands, actual response shapes, and clear triggers for when to hit each endpoint.

---

## What to document

Write a single markdown file: `docs/spells-api-handback.md` in this repo.

### Required sections

**1. Authentication**
- Confirm the service-token flow from the existing rulemaster skill still applies (same endpoint, same secret, same 30-day expiry) — or flag if Spell endpoints use a different policy.
- Give one working curl example that obtains a token and calls `GET /api/spells`.

**2. Endpoints — one block per endpoint**

For each of these, document: method + path, query params (with types and defaults), auth requirement, **real response JSON from a live call**, and trigger phrases ("use when user asks about...").

- `GET /api/spells`
- `GET /api/spells/{id}`
- `GET /api/spells/by-game/{gameId}`
- `GET /api/spells/usable?mageLevel={n}&gameId={id}`
- `GET /api/spells/learnable?mageLevel={n}&buildingId={id}`
- `PUT /api/spells/{id}/buildings`
- `POST /api/spells` (admin)
- `PUT /api/spells/{id}` (admin)
- `DELETE /api/spells/{id}` (admin)
- `GET /api/search?q=...` — show a response where the match set includes a Spell entry, call out the `entityType` value you return ("Spell")

Use **actual response JSON** — run the endpoint against your local DB with the seeded data and paste the real body. Pretty-print. Include one success case per endpoint; include one edge case (not-found, empty result, or class/level filter boundary).

**3. DTO shapes**

For each DTO exposed by the API (`SpellDto`, `SpellListDto`, `SpellCreateDto`, `SpellUpdateDto`, and any nested building/game DTOs), write:
- Full field list with C# type → JSON type mapping
- Which fields are nullable in JSON
- Which fields are required on create vs. optional on update
- Any enum string values (e.g. `SpellSchool` — list all 9 values as they serialize)

**4. Enum reference**

Print the full `SpellSchool` enum as JSON would see it (names + any int values if `[JsonConverter(typeof(JsonStringEnumConverter))]` is not applied). Same for any other enum you introduce.

**5. Filtering math — worked examples**

Give 4-5 concrete worked examples showing the expected result count for `usable` and `learnable` filters:

| Query | Expected result |
|-------|-----------------|
| `usable?mageLevel=0` | 6 spells (scrolls only) |
| `usable?mageLevel=2` | 14 spells (6 scrolls + 4 level-I + 4 level-II) |
| `learnable?mageLevel=3&buildingId={library}` | 12 spells (I + II + III, all linked to library) |
| ... | ... |

Include the actual seeded building IDs so the rulemaster skill can hard-reference them if needed (e.g. "library = BuildingId 17" — or flag that IDs are not stable and must be discovered via `GET /api/buildings`).

**6. Search integration**

Confirm whether `Spell` shows up in `/api/search` results with `entityType: "Spell"`. List which fields are indexed (Name, Effect, Description — per the tinkerer prompt). Give one example: `GET /api/search?q=ohnivá` and paste the response including spell matches.

**7. Sync-friendliness notes**

The rulemaster skill will soon gain a **Sync mode** that pulls everything from the DB and overwrites local `pravidla/` files for spells. Flag anything that matters for that workflow:
- Is there an `updated-at` / `last-modified` timestamp on `Spell` rows? If not, say so — rulemaster will have to full-sync each time.
- Is there an ETag or If-Modified-Since support? If not, say so.
- Is there a bulk `GET /api/spells/all-detail` that returns every spell with its `BuildingRequirements` inlined, or must the client do N+1 calls?
- Pagination: does `GET /api/spells` paginate, and if so, what's the default page size and how do you request all?

**8. Known gotchas**

Anything you discovered while implementing — quirks that would bite a first-time API consumer. Diacritics handling in search? Case sensitivity on enum values? Empty string vs. null? Document it.

---

## What NOT to do

- **Don't** copy the tinkerer prompt or the `spells.md` catalog — the rulemaster skill already knows the data shape.
- **Don't** write prose explaining what a spell is — the rulemaster skill owns the rule semantics. This is pure API reference.
- **Don't** guess at responses. If an endpoint returns a shape you haven't verified, call it live and paste the real output.
- **Don't** edit the rulemaster skill file yourself.

---

## Output

One file: `docs/spells-api-handback.md`

Length target: 300-500 lines. Dense reference, not a tutorial. When this is done, reply with:
- File path
- A one-paragraph summary of any surprises (things that differ from the tinkerer prompt, compromises you made, items flagged for follow-up)
- A list of any endpoints you added or dropped vs. the original prompt, with reason
