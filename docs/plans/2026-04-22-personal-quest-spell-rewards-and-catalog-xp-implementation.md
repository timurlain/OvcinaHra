# PersonalQuest — Spell Rewards + Catalog-Level XP Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Design:** `docs/plans/2026-04-22-personal-quest-spell-rewards-and-catalog-xp-design.md` (required reading — reference its sections where cited)

**Goal:** Add Spell as a third PersonalQuest reward type (many-to-many, non-learnable only, scrolls allow Quantity>1) and migrate XP from per-game-only to catalog-default-with-per-game-override.

**Architecture:** Purely additive domain extension. New `PersonalQuestSpellReward` join table mirrors `PersonalQuestItemReward`. `PersonalQuest.XpCost` becomes the intrinsic catalog value; `GamePersonalQuest.XpCost` goes nullable as an optional override. UI stays in the existing `PersonalQuestList.razor` popup (wizard is out of scope — Phase 2).

**Tech Stack:** .NET 10, ASP.NET Core Minimal API, EF Core 10 + Npgsql, Blazor WASM + DevExpress Blazor 25.2.5, xUnit + Testcontainers PostgreSQL.

**Branch:** `feat/personal-quest-spell-rewards-and-catalog-xp` (design doc already committed as `02ce111`).

**Project conventions to remember during execution:**
- TDD every time: write the failing test, run it red, write the minimal code, run it green, commit. Don't skip the red step.
- Every commit subject includes `[v0.10.0]` suffix. Version bump to `0.10.0` is a dedicated task at the end; intermediate commits still tag the target version.
- Run `dotnet test` from the repo root — must be green before advancing to the next task.
- UI strings in Czech with diacritics. Routes, identifiers, class names, commit subjects in English.
- DevExpress DxGrid always via the `OvcinaGrid` wrapper. When the grid column schema changes, bump the `LayoutKey` suffix — otherwise users' cached localStorage filters replay against the new schema (see MEMORY: "OvcinaGrid LayoutKey bump").
- No destructive-action confirmations required for reward-badge remove buttons (existing Items section uses plain click — match that pattern; see MEMORY: "Destructive action confirm" — reward badges are explicitly NOT in scope for confirm popups).
- EF Core migrations auto-apply on API startup via `db.Database.MigrateAsync()`. No manual migrate step at deploy.
- Server-generated migrations live in `src/OvcinaHra.Api/Migrations/`. Generate with `dotnet ef migrations add <Name> --project src/OvcinaHra.Api --startup-project src/OvcinaHra.Api --output-dir Migrations`.
- Configurations auto-register via `ApplyConfigurationsFromAssembly` in `WorldDbContext.OnModelCreating`. Adding a config file is enough — no manual registration line.

**Working directory for all commands:** `C:\Users\TomášPajonk\source\repos\timurlain\ovcinahra`.

---

## Task 1 — Add `XpCost` to catalog `PersonalQuest` entity

**Covers design section:** "Domain model changes → Modified entity: PersonalQuest".

**Files:**
- Modify: `src/OvcinaHra.Shared/Domain/Entities/PersonalQuest.cs:21` (add the new property after `ImagePath`)
- Modify: `src/OvcinaHra.Shared/Dtos/PersonalQuestDtos.cs` (add `XpCost` to `PersonalQuestListDto`, `PersonalQuestDetailDto`, `CreatePersonalQuestDto`, `UpdatePersonalQuestDto`)
- Modify: `src/OvcinaHra.Api/Endpoints/PersonalQuestEndpoints.cs` (map `XpCost` in Create, Update, Get-by-id, List, List-by-game projections)
- Test: `tests/OvcinaHra.Api.Tests/Endpoints/PersonalQuestEndpointTests.cs` (add `CreateQuest_WithXpCost_Persisted`)

### Step 1: Write the failing test

Append this test to `PersonalQuestEndpointTests.cs`:

```csharp
[Fact]
public async Task CreateQuest_WithXpCost_Persisted()
{
    var create = new CreatePersonalQuestDto(
        Name: "Test XP Quest",
        Difficulty: TreasureQuestDifficulty.Early,
        Description: null,
        AllowWarrior: true, AllowArcher: false, AllowMage: false, AllowThief: false,
        QuestCardText: null, RewardCardText: null, RewardNote: null, Notes: null,
        XpCost: 12);

    var resp = await Client.PostAsJsonAsync("/api/personal-quests", create);
    Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

    var body = await resp.Content.ReadFromJsonAsync<PersonalQuestDetailDto>();
    Assert.NotNull(body);
    Assert.Equal(12, body.XpCost);
}
```

### Step 2: Run the test — it MUST fail

```
dotnet test tests/OvcinaHra.Api.Tests/OvcinaHra.Api.Tests.csproj --filter FullyQualifiedName~CreateQuest_WithXpCost_Persisted
```
Expected: compile error ("`CreatePersonalQuestDto` does not contain a definition for `XpCost`" and "`PersonalQuestDetailDto` does not contain a definition for `XpCost`"). That's the red.

### Step 3: Minimal implementation

Add to `PersonalQuest.cs` immediately below `public string? ImagePath`:

```csharp
    public int XpCost { get; set; }
```

In `PersonalQuestDtos.cs`, add `int XpCost` as the last positional parameter on the four records:
- `PersonalQuestListDto`
- `PersonalQuestDetailDto`
- `CreatePersonalQuestDto`
- `UpdatePersonalQuestDto`

In `PersonalQuestEndpoints.cs`:
- In the `Create` handler body, include `XpCost = dto.XpCost` on the new entity
- In the `Update` handler body, include `entity.XpCost = dto.XpCost`
- In the projection used by the list handler (`GetAll`) and `GetById`, add `XpCost = q.XpCost`
- In the by-game projection, include `XpCost = gl.PersonalQuest.XpCost` (the catalog value — this is not the override; the override comes in Task 2 via `EffectiveXpCost`)

### Step 4: Run the test — MUST pass, AND the existing 22 tests must stay green

```
dotnet test
```
Expected: 23 passing, 0 failing.

### Step 5: Commit

```bash
git add src/OvcinaHra.Shared/Domain/Entities/PersonalQuest.cs \
        src/OvcinaHra.Shared/Dtos/PersonalQuestDtos.cs \
        src/OvcinaHra.Api/Endpoints/PersonalQuestEndpoints.cs \
        tests/OvcinaHra.Api.Tests/Endpoints/PersonalQuestEndpointTests.cs
git commit -m "feat(personal-quest): add XpCost to catalog entity + DTOs [v0.10.0]"
```

> **Migration note deferred:** we don't generate the EF migration until Task 3 so that all schema changes end up in a single migration file `AddPersonalQuestSpellRewardsAndCatalogXp`. The test database is brought up by the existing test fixture via `EnsureCreated` / applied migrations — in this repo the fixture applies pending migrations on startup, so the new column will not exist for the test run yet. **If the test fails at runtime with "column XpCost does not exist"**, jump to Task 3 first (generate migration) and come back. Recheck in the test fixture (`tests/OvcinaHra.Api.Tests/Fixtures/PostgresFixture.cs`) whether it uses `MigrateAsync` or `EnsureCreatedAsync`. If the latter, the column is created from the current model and this caveat is moot.

---

## Task 2 — Make `GamePersonalQuest.XpCost` nullable with `EffectiveXpCost`

**Covers design section:** "Domain model changes → Modified entity: GamePersonalQuest" + "DTO changes".

**Files:**
- Modify: `src/OvcinaHra.Shared/Domain/Entities/GamePersonalQuest.cs:7` (XpCost `int` → `int?`)
- Modify: `src/OvcinaHra.Shared/Dtos/PersonalQuestDtos.cs` (GamePersonalQuestListDto.XpCost → `int?`; add `int EffectiveXpCost`; CreateGamePersonalQuestDto.XpCost → `int?`; UpdateGamePersonalQuestDto.XpCost → `int?`)
- Modify: `src/OvcinaHra.Api/Endpoints/PersonalQuestEndpoints.cs` (by-game projection computes `EffectiveXpCost = gl.XpCost ?? gl.PersonalQuest.XpCost`; Create/UpdateGameLink map nullable XpCost)
- Modify: `src/OvcinaHra.Api/Data/Configurations/GamePersonalQuestConfiguration.cs` (if it has `.IsRequired()` on XpCost, remove it or set `.IsRequired(false)`)
- Test: `tests/OvcinaHra.Api.Tests/Endpoints/PersonalQuestEndpointTests.cs` (update 5 existing tests that reference XpCost; add 2 new tests)

### Step 1: Write the 2 failing tests

Append:

```csharp
[Fact]
public async Task GamePersonalQuest_XpCostNull_EffectiveFallsBackToCatalog()
{
    // Arrange: create a game, create a quest with catalog XpCost=42, link with override=null
    var game = await CreateGameAsync();
    var quest = await CreatePersonalQuestAsync(xpCost: 42);

    var link = await Client.PostAsJsonAsync("/api/personal-quests/game-link",
        new CreateGamePersonalQuestDto(GameId: game.Id, PersonalQuestId: quest.Id,
                                       XpCost: null, PerKingdomLimit: null));
    Assert.Equal(HttpStatusCode.Created, link.StatusCode);

    // Act
    var list = await Client.GetFromJsonAsync<List<GamePersonalQuestListDto>>(
        $"/api/personal-quests/by-game/{game.Id}");
    Assert.NotNull(list);
    var row = Assert.Single(list);

    // Assert
    Assert.Null(row.XpCost);
    Assert.Equal(42, row.EffectiveXpCost);
}

[Fact]
public async Task GamePersonalQuest_XpCostSet_EffectiveEqualsOverride()
{
    var game = await CreateGameAsync();
    var quest = await CreatePersonalQuestAsync(xpCost: 42);

    await Client.PostAsJsonAsync("/api/personal-quests/game-link",
        new CreateGamePersonalQuestDto(game.Id, quest.Id, XpCost: 7, PerKingdomLimit: null));

    var list = await Client.GetFromJsonAsync<List<GamePersonalQuestListDto>>(
        $"/api/personal-quests/by-game/{game.Id}");
    var row = Assert.Single(list!);

    Assert.Equal(7, row.XpCost);
    Assert.Equal(7, row.EffectiveXpCost);
}
```

If `CreatePersonalQuestAsync` and `CreateGameAsync` helpers don't already exist in the test file, add them as small private helpers (POST to `/api/personal-quests` or `/api/games` and return the deserialized detail DTO). Check if similar helpers already exist before duplicating — there are 22 existing tests, patterns are established.

### Step 2: Run — the 2 new tests fail (compile error on `EffectiveXpCost`; nullable type mismatch on `XpCost`)

```
dotnet test tests/OvcinaHra.Api.Tests/OvcinaHra.Api.Tests.csproj --filter FullyQualifiedName~GamePersonalQuest_XpCost
```

### Step 3: Minimal implementation

1. `GamePersonalQuest.cs` line 7: `public int XpCost { get; set; }` → `public int? XpCost { get; set; }`

2. `GamePersonalQuestConfiguration.cs`: if there's a `.Property(x => x.XpCost).IsRequired()`, change to `.IsRequired(false)` or remove. If nothing was specified, EF defaults to nullable for `int?`.

3. `PersonalQuestDtos.cs`:
   - `GamePersonalQuestListDto.XpCost`: `int` → `int?`
   - Add new positional parameter `int EffectiveXpCost` to `GamePersonalQuestListDto`
   - `CreateGamePersonalQuestDto.XpCost`: `int = 0` → `int? = null`
   - `UpdateGamePersonalQuestDto.XpCost`: `int` → `int?`

4. `PersonalQuestEndpoints.cs`:
   - In the by-game list projection (around line 200–214 per survey), add `EffectiveXpCost = gl.XpCost ?? gl.PersonalQuest.XpCost` and forward the nullable `XpCost` through
   - `CreateGameLink` handler: accept nullable XpCost — no code change needed if you just pass `dto.XpCost` through
   - `UpdateGameLink` handler: same — the `int? = ?` flows through

5. Update the 5 existing tests that hit XpCost (from survey: lines 154, 184, 256, 337, 389):
   - Where a test posts `XpCost: 15` (int), it still works — int is implicitly convertible to `int?`
   - Where a test asserts `Assert.Equal(15, row.XpCost)` with `row.XpCost` now `int?`, change to `Assert.Equal(15, row.XpCost)` — xUnit will promote, no change needed usually. If there's a type inference error, use `Assert.Equal<int?>(15, row.XpCost)` or assert `row.EffectiveXpCost` instead where semantically appropriate.
   - `UpdateGameLink_TunesXpCost` (line 389): the test updates XpCost 10 → 25. Still valid with nullable. If it asserts a non-null value, that stays correct.

### Step 4: Run — all tests pass

```
dotnet test
```
Expected: 25 passing, 0 failing.

### Step 5: Commit

```bash
git add src/OvcinaHra.Shared/Domain/Entities/GamePersonalQuest.cs \
        src/OvcinaHra.Shared/Dtos/PersonalQuestDtos.cs \
        src/OvcinaHra.Api/Endpoints/PersonalQuestEndpoints.cs \
        src/OvcinaHra.Api/Data/Configurations/GamePersonalQuestConfiguration.cs \
        tests/OvcinaHra.Api.Tests/Endpoints/PersonalQuestEndpointTests.cs
git commit -m "feat(personal-quest): nullable game-level XpCost + EffectiveXpCost fallback [v0.10.0]"
```

---

## Task 3 — Add `PersonalQuestSpellReward` entity + EF migration

**Covers design section:** "Domain model changes → New entity: PersonalQuestSpellReward" + "Migration strategy".

**Files:**
- Create: `src/OvcinaHra.Shared/Domain/Entities/PersonalQuestSpellReward.cs`
- Create: `src/OvcinaHra.Api/Data/Configurations/PersonalQuestSpellRewardConfiguration.cs`
- Modify: `src/OvcinaHra.Shared/Domain/Entities/PersonalQuest.cs` (add `SpellRewards` navigation)
- Modify: `src/OvcinaHra.Api/Data/WorldDbContext.cs:54` (add `DbSet<PersonalQuestSpellReward>` right after `PersonalQuestItemRewards`)
- Generate: `src/OvcinaHra.Api/Migrations/<timestamp>_AddPersonalQuestSpellRewardsAndCatalogXp.cs`

### Step 1: Create the entity

`src/OvcinaHra.Shared/Domain/Entities/PersonalQuestSpellReward.cs`:

```csharp
namespace OvcinaHra.Shared.Domain.Entities;

public class PersonalQuestSpellReward
{
    public int PersonalQuestId { get; set; }
    public int SpellId { get; set; }
    public int Quantity { get; set; } = 1;

    public PersonalQuest PersonalQuest { get; set; } = null!;
    public Spell Spell { get; set; } = null!;
}
```

### Step 2: Create the configuration (mirrors `PersonalQuestItemRewardConfiguration`)

`src/OvcinaHra.Api/Data/Configurations/PersonalQuestSpellRewardConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OvcinaHra.Shared.Domain.Entities;

namespace OvcinaHra.Api.Data.Configurations;

public class PersonalQuestSpellRewardConfiguration : IEntityTypeConfiguration<PersonalQuestSpellReward>
{
    public void Configure(EntityTypeBuilder<PersonalQuestSpellReward> b)
    {
        b.HasKey(e => new { e.PersonalQuestId, e.SpellId });
        b.Property(e => e.Quantity).HasDefaultValue(1);
        b.ToTable(t => t.HasCheckConstraint("CK_PQSpellReward_Qty_Positive", "\"Quantity\" >= 1"));
        b.HasOne(e => e.PersonalQuest).WithMany(q => q.SpellRewards)
            .HasForeignKey(e => e.PersonalQuestId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(e => e.Spell).WithMany()
            .HasForeignKey(e => e.SpellId).OnDelete(DeleteBehavior.Restrict);
    }
}
```

### Step 3: Add the navigation on `PersonalQuest`

In `PersonalQuest.cs`, after `ItemRewards` collection:

```csharp
    public ICollection<PersonalQuestSpellReward> SpellRewards { get; set; } = [];
```

### Step 4: Register DbSet in `WorldDbContext.cs:54`

Add right after `PersonalQuestItemRewards`:

```csharp
public DbSet<PersonalQuestSpellReward> PersonalQuestSpellRewards => Set<PersonalQuestSpellReward>();
```

### Step 5: Generate the migration

```bash
dotnet ef migrations add AddPersonalQuestSpellRewardsAndCatalogXp \
  --project src/OvcinaHra.Api \
  --startup-project src/OvcinaHra.Api \
  --output-dir Migrations
```

Inspect the generated `_AddPersonalQuestSpellRewardsAndCatalogXp.cs`. Verify it contains:
- `AddColumn<int>(name: "XpCost", table: "PersonalQuests", ... defaultValue: 0)`
- `AlterColumn<int>(name: "XpCost", table: "GamePersonalQuests", ... nullable: true)`  _(or however EF names the table — check actual migration output)_
- `CreateTable(name: "PersonalQuestSpellRewards", ...)` with composite PK, FK to PersonalQuests (cascade), FK to Spells (restrict), Quantity default 1, CHECK constraint on Quantity

If any of these are missing, something in Tasks 1/2/3 wasn't saved. Don't hand-edit the migration unless you understand the fix — go back and correct the entity/config.

### Step 6: Build + run all tests (migrations run on fixture startup)

```
dotnet build
dotnet test
```
Expected: green (25 passing from Task 2).

### Step 7: Commit

```bash
git add src/OvcinaHra.Shared/Domain/Entities/PersonalQuestSpellReward.cs \
        src/OvcinaHra.Shared/Domain/Entities/PersonalQuest.cs \
        src/OvcinaHra.Api/Data/Configurations/PersonalQuestSpellRewardConfiguration.cs \
        src/OvcinaHra.Api/Data/WorldDbContext.cs \
        src/OvcinaHra.Api/Migrations/
git commit -m "feat(personal-quest): PersonalQuestSpellReward entity + migration [v0.10.0]"
```

---

## Task 4 — `POST /api/personal-quests/{id}/spell-rewards` — 201 on non-learnable spell

**Covers design section:** "API surface → POST /{id}/spell-rewards (201 branch)" + "DTO changes → AddSpellRewardDto".

**Files:**
- Modify: `src/OvcinaHra.Shared/Dtos/PersonalQuestDtos.cs` (add `AddSpellRewardDto`, `PersonalQuestSpellRewardSummary`)
- Modify: `src/OvcinaHra.Api/Endpoints/PersonalQuestEndpoints.cs` (add `MapPost("/{id:int}/spell-rewards", ...)` handler near the existing item/skill reward endpoints — around line 28–31 per survey)
- Test: `tests/OvcinaHra.Api.Tests/Endpoints/PersonalQuestEndpointTests.cs`

### Step 1: Write the failing test

```csharp
[Fact]
public async Task SpellReward_NonLearnable_Returns201()
{
    var quest = await CreatePersonalQuestAsync();
    var spell = await CreateSpellAsync(name: "Test scroll",
                                       isScroll: true, isLearnable: false, minMageLevel: 0);

    var resp = await Client.PostAsJsonAsync(
        $"/api/personal-quests/{quest.Id}/spell-rewards",
        new AddSpellRewardDto(spell.Id, Quantity: 1));

    Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

    var detail = await Client.GetFromJsonAsync<PersonalQuestDetailDto>(
        $"/api/personal-quests/{quest.Id}");
    Assert.NotNull(detail);
    Assert.Single(detail.SpellRewards);
    Assert.Equal(spell.Id, detail.SpellRewards[0].SpellId);
    Assert.Equal(1, detail.SpellRewards[0].Quantity);
}
```

Add a `CreateSpellAsync` helper if not present — it POSTs to `/api/spells` using `CreateSpellDto`.

### Step 2: Run — FAIL (compile error on `AddSpellRewardDto`, `SpellRewards` on detail DTO)

### Step 3: Implement

In `PersonalQuestDtos.cs`, add:

```csharp
public record AddSpellRewardDto(int SpellId, int Quantity = 1);

public record PersonalQuestSpellRewardSummary(int SpellId, string SpellName, bool IsScroll, int Quantity);
```

Extend `PersonalQuestDetailDto` with:
```csharp
IReadOnlyList<PersonalQuestSpellRewardSummary> SpellRewards
```

Update `PersonalQuestEndpoints.cs`:
- In the `GetById` / detail projection, include:
  ```csharp
  SpellRewards = q.SpellRewards
      .Select(sr => new PersonalQuestSpellRewardSummary(sr.SpellId, sr.Spell.Name, sr.Spell.IsScroll, sr.Quantity))
      .ToList()
  ```
- Add a new endpoint mapper next to `AddItemReward`:
  ```csharp
  pq.MapPost("/{id:int}/spell-rewards", AddSpellReward);
  ```
- Implement the handler (happy path only for this task — validation comes in Task 5):
  ```csharp
  static async Task<IResult> AddSpellReward(int id, AddSpellRewardDto dto, WorldDbContext db)
  {
      if (!await db.PersonalQuests.AnyAsync(q => q.Id == id))
          return TypedResults.NotFound($"Quest #{id} neexistuje.");

      var spell = await db.Spells.FindAsync(dto.SpellId);
      if (spell is null)
          return TypedResults.NotFound($"Kouzlo #{dto.SpellId} neexistuje.");

      var link = new PersonalQuestSpellReward
      {
          PersonalQuestId = id,
          SpellId = dto.SpellId,
          Quantity = dto.Quantity
      };
      db.PersonalQuestSpellRewards.Add(link);
      await db.SaveChangesAsync();

      return TypedResults.Created($"/api/personal-quests/{id}/spell-rewards/{dto.SpellId}");
  }
  ```

### Step 4: Run — test passes

```
dotnet test
```

### Step 5: Commit

```bash
git add src/OvcinaHra.Shared/Dtos/PersonalQuestDtos.cs \
        src/OvcinaHra.Api/Endpoints/PersonalQuestEndpoints.cs \
        tests/OvcinaHra.Api.Tests/Endpoints/PersonalQuestEndpointTests.cs
git commit -m "feat(personal-quest): POST spell-rewards endpoint (non-learnable path) [v0.10.0]"
```

---

## Task 5 — `POST /spell-rewards` returns 400 for learnable spell

**Covers design section:** "API surface — 400 when `spell.IsLearnable == true`".

**Files:**
- Modify: `src/OvcinaHra.Api/Endpoints/PersonalQuestEndpoints.cs` (validation branch in `AddSpellReward`)
- Test: `tests/OvcinaHra.Api.Tests/Endpoints/PersonalQuestEndpointTests.cs`

### Step 1: Write the failing test

```csharp
[Fact]
public async Task SpellReward_LearnableSpell_Returns400()
{
    var quest = await CreatePersonalQuestAsync();
    var spell = await CreateSpellAsync(name: "Učenlivé kouzlo",
                                       isScroll: false, isLearnable: true, minMageLevel: 1);

    var resp = await Client.PostAsJsonAsync(
        $"/api/personal-quests/{quest.Id}/spell-rewards",
        new AddSpellRewardDto(spell.Id));

    Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    var detail = await Client.GetFromJsonAsync<PersonalQuestDetailDto>(
        $"/api/personal-quests/{quest.Id}");
    Assert.Empty(detail!.SpellRewards);
}
```

### Step 2: Run — FAIL (currently the endpoint returns 201 because no validation)

### Step 3: Implement

In `AddSpellReward`, between the spell-not-found check and the insert:

```csharp
if (spell.IsLearnable)
    return TypedResults.BadRequest($"Kouzlo #{dto.SpellId} je naučitelné — nelze ho použít jako odměnu personal questu.");
```

### Step 4: Run — green

### Step 5: Commit

```bash
git add src/OvcinaHra.Api/Endpoints/PersonalQuestEndpoints.cs \
        tests/OvcinaHra.Api.Tests/Endpoints/PersonalQuestEndpointTests.cs
git commit -m "feat(personal-quest): reject learnable-spell reward with 400 [v0.10.0]"
```

---

## Task 6 — Quantity > 1 persists for scroll rewards

**Covers design section:** "Spell reward quantity" (UI hides, API accepts).

**Files:**
- Test: `tests/OvcinaHra.Api.Tests/Endpoints/PersonalQuestEndpointTests.cs` (new test only — likely no production code change, this is a verification)

### Step 1: Write the test

```csharp
[Fact]
public async Task SpellReward_Scroll_QuantityGreaterThanOne_Persisted()
{
    var quest = await CreatePersonalQuestAsync();
    var scroll = await CreateSpellAsync("Svitek ohnivého šípu",
                                        isScroll: true, isLearnable: false, minMageLevel: 0);

    var resp = await Client.PostAsJsonAsync(
        $"/api/personal-quests/{quest.Id}/spell-rewards",
        new AddSpellRewardDto(scroll.Id, Quantity: 3));
    Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

    var detail = await Client.GetFromJsonAsync<PersonalQuestDetailDto>(
        $"/api/personal-quests/{quest.Id}");
    var reward = Assert.Single(detail!.SpellRewards);
    Assert.Equal(3, reward.Quantity);
    Assert.True(reward.IsScroll);
}
```

### Step 2: Run

Expected: PASS without code changes (endpoint from Task 4 already persists Quantity; Task 5 doesn't touch it). If it fails, fix the projection to include `IsScroll` from `sr.Spell.IsScroll`.

### Step 3: Commit

```bash
git add tests/OvcinaHra.Api.Tests/Endpoints/PersonalQuestEndpointTests.cs
git commit -m "test(personal-quest): verify scroll-reward quantity persists [v0.10.0]"
```

---

## Task 7 — `DELETE /api/personal-quests/{id}/spell-rewards/{spellId}` returns 204

**Covers design section:** "API surface → DELETE /{id}/spell-rewards/{spellId}".

**Files:**
- Modify: `src/OvcinaHra.Api/Endpoints/PersonalQuestEndpoints.cs`
- Test: `tests/OvcinaHra.Api.Tests/Endpoints/PersonalQuestEndpointTests.cs`

### Step 1: Write the failing test

```csharp
[Fact]
public async Task Delete_SpellReward_Returns204()
{
    var quest = await CreatePersonalQuestAsync();
    var spell = await CreateSpellAsync("Svitek bleskového úderu",
                                        isScroll: true, isLearnable: false, minMageLevel: 0);

    await Client.PostAsJsonAsync($"/api/personal-quests/{quest.Id}/spell-rewards",
        new AddSpellRewardDto(spell.Id));

    var del = await Client.DeleteAsync(
        $"/api/personal-quests/{quest.Id}/spell-rewards/{spell.Id}");
    Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

    var detail = await Client.GetFromJsonAsync<PersonalQuestDetailDto>(
        $"/api/personal-quests/{quest.Id}");
    Assert.Empty(detail!.SpellRewards);
}
```

### Step 2: Run — FAIL (404, endpoint missing)

### Step 3: Implement

Add mapper near the delete-item-reward line:

```csharp
pq.MapDelete("/{id:int}/spell-rewards/{spellId:int}", RemoveSpellReward);
```

Handler:

```csharp
static async Task<IResult> RemoveSpellReward(int id, int spellId, WorldDbContext db)
{
    var link = await db.PersonalQuestSpellRewards
        .FirstOrDefaultAsync(x => x.PersonalQuestId == id && x.SpellId == spellId);
    if (link is null) return TypedResults.NotFound();

    db.PersonalQuestSpellRewards.Remove(link);
    await db.SaveChangesAsync();
    return TypedResults.NoContent();
}
```

### Step 4: Run — green

### Step 5: Commit

```bash
git add src/OvcinaHra.Api/Endpoints/PersonalQuestEndpoints.cs \
        tests/OvcinaHra.Api.Tests/Endpoints/PersonalQuestEndpointTests.cs
git commit -m "feat(personal-quest): DELETE spell-reward endpoint [v0.10.0]"
```

---

## Task 8 — Expose `SpellRewards` on list DTOs and roll into `RewardSummary`

**Covers design section:** "DTO changes → PersonalQuestListDto / DetailDto additions" + "RewardSummary server-side".

**Files:**
- Modify: `src/OvcinaHra.Shared/Dtos/PersonalQuestDtos.cs` (add `SpellRewards` to `PersonalQuestListDto`)
- Modify: `src/OvcinaHra.Api/Endpoints/PersonalQuestEndpoints.cs` (list + by-game projections include SpellRewards; `BuildRewardSummary` appends spell rewards)
- Test: extend `GetByGame_MultipleLinked_ReturnsWithRewardSummary` or add a focused assertion

### Step 1: Focused test addition

Append a minimal assertion to the existing `GetByGame_MultipleLinked_ReturnsWithRewardSummary` test, OR add a new test:

```csharp
[Fact]
public async Task GetByGame_QuestWithSpellReward_IncludesSpellInRewardSummary()
{
    var game = await CreateGameAsync();
    var quest = await CreatePersonalQuestAsync(xpCost: 5);
    var scroll = await CreateSpellAsync("Svitek léčení",
                                         isScroll: true, isLearnable: false, minMageLevel: 0);
    await Client.PostAsJsonAsync($"/api/personal-quests/{quest.Id}/spell-rewards",
        new AddSpellRewardDto(scroll.Id, Quantity: 2));
    await Client.PostAsJsonAsync("/api/personal-quests/game-link",
        new CreateGamePersonalQuestDto(game.Id, quest.Id, XpCost: null, PerKingdomLimit: null));

    var list = await Client.GetFromJsonAsync<List<GamePersonalQuestListDto>>(
        $"/api/personal-quests/by-game/{game.Id}");
    var row = Assert.Single(list!);
    Assert.NotNull(row.RewardSummary);
    Assert.Contains("Svitek léčení", row.RewardSummary);
    Assert.Contains("×2", row.RewardSummary);
}
```

### Step 2: Run — likely FAIL (summary doesn't include spells yet)

### Step 3: Implement

Add `SpellRewards` (list of `PersonalQuestSpellRewardSummary`) to `PersonalQuestListDto`.

In both list projections (catalog list and by-game list), include spell rewards:
```csharp
SpellRewards = q.SpellRewards
    .Select(sr => new PersonalQuestSpellRewardSummary(sr.SpellId, sr.Spell.Name, sr.Spell.IsScroll, sr.Quantity))
    .ToList()
```

Update `BuildRewardSummary(q)` around line 200–214 to append spell names. Pattern (adapt to the actual existing code):
```csharp
parts.AddRange(q.SpellRewards.Select(sr =>
    sr.Quantity > 1 ? $"{sr.Spell.Name} ×{sr.Quantity}" : sr.Spell.Name));
```

### Step 4: Run — green

### Step 5: Commit

```bash
git add src/OvcinaHra.Shared/Dtos/PersonalQuestDtos.cs \
        src/OvcinaHra.Api/Endpoints/PersonalQuestEndpoints.cs \
        tests/OvcinaHra.Api.Tests/Endpoints/PersonalQuestEndpointTests.cs
git commit -m "feat(personal-quest): expose spell rewards on list + in RewardSummary [v0.10.0]"
```

---

## Task 9 — Client: XP spinner in the main quest form

**Covers design section:** "UI changes → 1. XP in the main form".

**Files:**
- Modify: `src/OvcinaHra.Client/Pages/PersonalQuests/PersonalQuestList.razor` (around line 153 — the Difficulty `DxFormLayoutItem`; and the code-behind `@code` block to bind `XpCost`)

### Step 1: Add UI markup

Locate the `DxFormLayoutItem` for Difficulty at line 153. Insert a new item immediately after it:

```razor
<DxFormLayoutItem Caption="XP cena" ColSpanMd="6">
    <DxSpinEdit @bind-Value="@model.XpCost" MinValue="0" MaxValue="999" />
</DxFormLayoutItem>
```

Consider setting Difficulty's `ColSpanMd="6"` so the two sit side-by-side on one row. If Difficulty already occupies `ColSpanMd="12"`, shrink it.

### Step 2: Update `@code` model class

Wherever the page defines its edit model (there's likely an `EditModel` class or set of private fields), add:
- `public int XpCost { get; set; }` on the model
- When loading a quest into the model (open-popup handler), map `detail.XpCost` in
- When saving (PUT/POST), include `XpCost = model.XpCost` in the DTO payload

### Step 3: Verify build and runtime

```
dotnet build
```

Manual runtime check (no automated UI test):
1. `./devStart.bat` or `dotnet run --project src/OvcinaHra.Client`
2. Open `/personal-quests`, edit a quest, confirm the XP spinner shows and persists

### Step 4: Commit

```bash
git add src/OvcinaHra.Client/Pages/PersonalQuests/PersonalQuestList.razor
git commit -m "feat(personal-quest): XP spinner in main quest form [v0.10.0]"
```

---

## Task 10 — Client: per-game XP spinner becomes nullable with fallback hint

**Covers design section:** "UI changes → 2. Per-game card XP override".

**Files:**
- Modify: `src/OvcinaHra.Client/Pages/PersonalQuests/PersonalQuestList.razor` (lines 276–315 — per-game config card; the code-behind field `gpqXpCost`)

### Step 1: Code-behind change

Change the field declaration from `private int gpqXpCost;` to `private int? gpqXpCost;`.

When loading the GamePersonalQuest linked row, map `row.XpCost` (now `int?`) directly to `gpqXpCost`.

When saving (PUT), pass `gpqXpCost` directly as the nullable value.

### Step 2: Update the two `DxSpinEdit` occurrences (lines ~288, ~303)

Replace with:

```razor
<DxSpinEdit @bind-Value="@gpqXpCost" MinValue="0"
            NullText="@($"výchozí: {model.XpCost}")"
            ClearButtonDisplayMode="DataEditorClearButtonDisplayMode.Auto" />
```

Below the spinner, add the hint-when-null:

```razor
@if (gpqXpCost is null)
{
    <div class="form-text small">
        Bez přepisu — pro tuto hru platí výchozí @model.XpCost XP z katalogu.
    </div>
}
```

### Step 3: Build + runtime check

```
dotnet build
```

### Step 4: Commit

```bash
git add src/OvcinaHra.Client/Pages/PersonalQuests/PersonalQuestList.razor
git commit -m "feat(personal-quest): per-game XP override with catalog-fallback hint [v0.10.0]"
```

---

## Task 11 — Client: Spell rewards section in the popup

**Covers design section:** "UI changes → 3. New Spell rewards section".

This is the largest UI task. Mirrors the Items reward section (starts line 236 per survey).

**Files:**
- Modify: `src/OvcinaHra.Client/Pages/PersonalQuests/PersonalQuestList.razor`

### Step 1: Code-behind additions

Add to the `@code` block:

```csharp
private List<PersonalQuestSpellRewardSummary> detailSpellRewards = [];
private List<SpellListDto> allSpells = []; // catalog of non-learnable spells
private int? newSpellId;
private int newSpellQuantity = 1;

private SpellListDto? SelectedAddableSpell =>
    allSpells.FirstOrDefault(s => s.Id == newSpellId);

private bool SelectedSpellIsScroll => SelectedAddableSpell?.IsScroll == true;

private IEnumerable<SpellListDto> SpellsNotYetAdded =>
    allSpells.Where(s =>
        !s.IsLearnable &&
        !detailSpellRewards.Any(r => r.SpellId == s.Id));
```

In the page initialisation (OnInitializedAsync or whatever loader sets up catalog reference lists), load:

```csharp
allSpells = await Api.GetListAsync<SpellListDto>("/api/spells");
```

When opening the popup for an existing quest, populate `detailSpellRewards` from `detail.SpellRewards`.

Add handlers:

```csharp
private async Task AddSpellRewardAsync()
{
    if (newSpellId is null) return;
    var qty = SelectedSpellIsScroll ? newSpellQuantity : 1;
    try
    {
        await Api.PostAsync<AddSpellRewardDto, object?>(
            $"/api/personal-quests/{editingId}/spell-rewards",
            new AddSpellRewardDto(newSpellId.Value, qty));
        // reload quest detail so detailSpellRewards refreshes
        var updated = await Api.GetAsync<PersonalQuestDetailDto>(
            $"/api/personal-quests/{editingId}");
        if (updated is not null) detailSpellRewards = updated.SpellRewards.ToList();
        newSpellId = null;
        newSpellQuantity = 1;
    }
    catch (Exception ex) { error = ex.Message; }
}

private async Task RemoveSpellRewardAsync(int spellId)
{
    try
    {
        var ok = await Api.DeleteAsync(
            $"/api/personal-quests/{editingId}/spell-rewards/{spellId}");
        if (ok) detailSpellRewards = detailSpellRewards
            .Where(r => r.SpellId != spellId).ToList();
    }
    catch (Exception ex) { error = ex.Message; }
}
```

### Step 2: Mark-up — new section

Insert right after the Items reward block (mirrors the Items section in structure):

```razor
@* --- Spell rewards (catalog-level, mirrors Items section) --- *@
<div class="mt-2">
    <strong class="small">Kouzla:</strong>
    @if (detailSpellRewards.Any())
    {
        <div class="mt-1">
            @foreach (var sr in detailSpellRewards)
            {
                <span class="badge bg-secondary me-1">
                    @sr.SpellName@(sr.Quantity > 1 ? $" ×{sr.Quantity}" : "")
                    <button class="btn btn-sm btn-link text-white p-0 ms-1"
                            @onclick="() => RemoveSpellRewardAsync(sr.SpellId)">
                        <i class="bi bi-x"></i>
                    </button>
                </span>
            }
        </div>
    }
    else
    {
        <span class="text-muted small ms-2">Žádná kouzla.</span>
    }
    <div class="d-flex gap-2 align-items-end mt-1">
        <div style="flex:1">
            <DxComboBox Data="@SpellsNotYetAdded" @bind-Value="@newSpellId"
                        TextFieldName="Name" ValueFieldName="Id"
                        NullText="(přidat kouzlo)" SearchMode="ListSearchMode.AutoSearch" />
        </div>
        @if (SelectedSpellIsScroll)
        {
            <div style="width:70px">
                <DxSpinEdit @bind-Value="@newSpellQuantity" MinValue="1" />
            </div>
        }
        <button class="btn btn-sm btn-outline-primary" style="height:34px"
                disabled="@(newSpellId is null)"
                @onclick="AddSpellRewardAsync">
            <i class="bi bi-plus me-1"></i>Přidat
        </button>
    </div>
</div>
```

### Step 3: Build + runtime check

```
dotnet build
```

Manual flow:
1. Open quest popup → Kouzla section appears with empty state
2. Combobox offers only `IsLearnable=false` spells
3. Select a scroll → quantity spinner appears → pick 3 → Přidat → badge "Name ×3" appears
4. Select a non-scroll non-learnable spell → no quantity spinner → Přidat → badge "Name" (no quantity)
5. Click × on badge → spell reward is removed

### Step 4: Commit

```bash
git add src/OvcinaHra.Client/Pages/PersonalQuests/PersonalQuestList.razor
git commit -m "feat(personal-quest): spell rewards section in detail popup [v0.10.0]"
```

---

## Task 12 — Client: `EffectiveXpCost` column + `LayoutKey` bump

**Covers design section:** "UI changes → 4. Game grid".

**Files:**
- Modify: `src/OvcinaHra.Client/Pages/PersonalQuests/PersonalQuestList.razor` (line 95 LayoutKey; add column to the game-view grid Columns block)

### Step 1: Bump the LayoutKey

Line 95:
```razor
LayoutKey="grid-layout-personal-quests-game"
```
→
```razor
LayoutKey="grid-layout-personal-quests-game-v2"
```

### Step 2: Add the EffectiveXpCost column

Find the existing `<DxGridDataColumn FieldName="XpCost" ...>` in the game-view grid's `Columns` block and:
- Rename its `FieldName` to `EffectiveXpCost` and keep caption `"XP cena"` (this is now the effective value shown by default)
- Add a second column for the raw override value, hidden-by-default:

```razor
<DxGridDataColumn FieldName="EffectiveXpCost" Caption="XP cena" Width="110px" />
<DxGridDataColumn FieldName="XpCost" Caption="XP (přepis)" Width="120px" Visible="false" />
```

### Step 3: Build

```
dotnet build
```

### Step 4: Commit

```bash
git add src/OvcinaHra.Client/Pages/PersonalQuests/PersonalQuestList.razor
git commit -m "feat(personal-quest): EffectiveXpCost grid column + LayoutKey v2 [v0.10.0]"
```

---

## Task 13 — Bump client version to `0.10.0`

**Covers design section:** "Version & deploy".

**Files:**
- Modify: `src/OvcinaHra.Client/OvcinaHra.Client.csproj:10`

### Step 1: Edit

```xml
<Version>0.10.0</Version>
```

### Step 2: Build + run all tests one final time

```
dotnet build
dotnet test
```

### Step 3: Commit

```bash
git add src/OvcinaHra.Client/OvcinaHra.Client.csproj
git commit -m "chore: bump client version to 0.10.0 [v0.10.0]"
```

---

## Post-tasks: PR & merge

1. Push the branch: `git push -u origin feat/personal-quest-spell-rewards-and-catalog-xp`
2. Open PR targeting `main`:
   ```bash
   gh pr create --base main --head feat/personal-quest-spell-rewards-and-catalog-xp \
     --title "feat(personal-quest): spell rewards + catalog XP [v0.10.0]" \
     --body "<summary referencing the design doc>"
   ```
3. Wait for CI (`build` + `build-and-test` must pass — branch protection enforces this).
4. Handle any Copilot review comments with fixup commits.
5. Squash-merge, delete branch.

## Things to avoid (captured from prior session learnings)

- **Don't hand-edit the generated migration file unless you fully understand it.** If a field is missing, go back to the entity/config and regenerate the migration by deleting the generated file and re-running `dotnet ef migrations add`.
- **Don't use `AllowFilter` on `DxGridDataColumn`** — not valid in DevExpress Blazor 25.2.5; compiles fine, crashes at render (see MEMORY: "DevExpress attr errors at runtime").
- **Don't skip the `LayoutKey` bump** in Task 12 — users' cached localStorage filters will replay against the new schema and confuse everyone.
- **Don't commit without the `[v0.10.0]` tag** — project convention, enforced nowhere in CI but caught at review.
- **Don't add destructive-confirm popups to reward badges** — the Items section doesn't have one and we want spells to match.
