# PersonalQuest Domain Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Ship the PersonalQuest domain end-to-end — catalog + per-game config + character-assignment data model + two grid views + detail popup — per the design at `docs/plans/2026-04-19-personal-quest-domain-design.md`.

**Architecture:** Follow the Item/Skill catalog + per-game join pattern the app already uses (PRs #30 Skills and #32 Items). Entities + EF configs + a single additive migration server-side; a Client razor page with catalog/game grid split, shared DTOs, and a `PersonalQuestService` API wrapper.

**Tech Stack:** .NET 10, EF Core + Npgsql, ASP.NET Core minimal APIs (TypedResults), xUnit + Testcontainers-PostgreSQL integration tests, Blazor WASM PWA, DevExpress DxGrid / DxPopup / DxFormLayout.

**Repo / branch:** `C:/Users/TomášPajonk/source/repos/timurlain/ovcinahra` on `feat/personal-quest-domain` (design doc already committed).

**Commit convention:** Czech-friendly, `[v0.6.0]` suffix on the final commit that merges. Every task commit should be conventional (`feat(pq):`, `test(pq):`, `chore(pq):`).

**Version bump at end:** Client 0.5.3 → **0.6.0** (minor — new domain entity).

---

## Phase 0 — Preflight

### Task 0.1: Confirm working tree is clean on the branch

**Step 1:** Verify we're on `feat/personal-quest-domain` and it's clean.
```bash
git branch --show-current     # expect: feat/personal-quest-domain
git status --short            # expect: empty
git log --oneline -2          # expect: design doc on top of e4d99f5 / 35d678c / 3897c57 era main
```
If not clean, stash or commit first — do NOT start implementation on a dirty tree.

### Task 0.2: Verify existing tests still pass on this base

**Step 1:** Run the API integration tests once as a baseline (Docker Desktop must be running for Testcontainers).
```bash
cd C:/Users/TomášPajonk/source/repos/timurlain/ovcinahra
dotnet test tests/OvcinaHra.Api.Tests -c Release --nologo
```
Expected: all green (176+ tests passing). If anything already fails, stop and investigate — don't start layering on a broken base.

---

## Phase 1 — Shared entities

All entity files live under `src/OvcinaHra.Shared/Domain/Entities/`.

### Task 1.1: `PersonalQuest` entity (catalog)

**Files:**
- Create: `src/OvcinaHra.Shared/Domain/Entities/PersonalQuest.cs`

**Step 1:** Write the entity — per design doc § *Entities → PersonalQuest (catalog)*.

```csharp
using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Shared.Domain.Entities;

public class PersonalQuest
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public TreasureQuestDifficulty Difficulty { get; set; }

    public bool AllowWarrior { get; set; }
    public bool AllowArcher { get; set; }
    public bool AllowMage { get; set; }
    public bool AllowThief { get; set; }

    public string? QuestCardText { get; set; }
    public string? RewardCardText { get; set; }
    public string? RewardNote { get; set; }
    public string? Notes { get; set; }
    public string? ImagePath { get; set; }

    public ICollection<PersonalQuestSkillReward> SkillRewards { get; set; } = [];
    public ICollection<PersonalQuestItemReward> ItemRewards { get; set; } = [];
    public ICollection<GamePersonalQuest> GameLinks { get; set; } = [];
    public ICollection<CharacterPersonalQuest> CharacterAssignments { get; set; } = [];
}
```

**Step 2:** Don't build yet — the navigation types don't exist. Next tasks add them.

### Task 1.2: Three remaining simple entities in one batch

**Files:**
- Create: `src/OvcinaHra.Shared/Domain/Entities/PersonalQuestSkillReward.cs`
- Create: `src/OvcinaHra.Shared/Domain/Entities/PersonalQuestItemReward.cs`
- Create: `src/OvcinaHra.Shared/Domain/Entities/GamePersonalQuest.cs`
- Create: `src/OvcinaHra.Shared/Domain/Entities/CharacterPersonalQuest.cs`

**Step 1:** Write each, small files, one at a time.

```csharp
// PersonalQuestSkillReward.cs
namespace OvcinaHra.Shared.Domain.Entities;
public class PersonalQuestSkillReward
{
    public int PersonalQuestId { get; set; }
    public int SkillId { get; set; }

    public PersonalQuest PersonalQuest { get; set; } = null!;
    public Skill Skill { get; set; } = null!;
}
```

```csharp
// PersonalQuestItemReward.cs
namespace OvcinaHra.Shared.Domain.Entities;
public class PersonalQuestItemReward
{
    public int PersonalQuestId { get; set; }
    public int ItemId { get; set; }
    public int Quantity { get; set; } = 1;

    public PersonalQuest PersonalQuest { get; set; } = null!;
    public Item Item { get; set; } = null!;
}
```

```csharp
// GamePersonalQuest.cs
namespace OvcinaHra.Shared.Domain.Entities;
public class GamePersonalQuest
{
    public int GameId { get; set; }
    public int PersonalQuestId { get; set; }
    public int XpCost { get; set; }
    public int? PerKingdomLimit { get; set; }

    public Game Game { get; set; } = null!;
    public PersonalQuest PersonalQuest { get; set; } = null!;
}
```

```csharp
// CharacterPersonalQuest.cs
namespace OvcinaHra.Shared.Domain.Entities;
public class CharacterPersonalQuest
{
    public int CharacterId { get; set; }   // PK, one-to-one: a character has at most 1
    public int PersonalQuestId { get; set; }
    public DateTime AssignedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }

    public Character Character { get; set; } = null!;
    public PersonalQuest PersonalQuest { get; set; } = null!;
}
```

**Step 2:** Build to confirm Shared compiles.
```bash
dotnet build src/OvcinaHra.Shared/OvcinaHra.Shared.csproj -c Release --nologo -v:q
```
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

**Step 3:** Commit.
```bash
git add src/OvcinaHra.Shared/Domain/Entities/PersonalQuest*.cs \
        src/OvcinaHra.Shared/Domain/Entities/GamePersonalQuest.cs \
        src/OvcinaHra.Shared/Domain/Entities/CharacterPersonalQuest.cs
git commit -m "feat(pq): add PersonalQuest + reward/link entities"
```

---

## Phase 2 — EF Core configurations

All config files live under `src/OvcinaHra.Api/Data/Configurations/`.

### Task 2.1: `PersonalQuestConfiguration`

**Files:**
- Create: `src/OvcinaHra.Api/Data/Configurations/PersonalQuestConfiguration.cs`

**Step 1:** Write:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OvcinaHra.Shared.Domain.Entities;

namespace OvcinaHra.Api.Data.Configurations;

public class PersonalQuestConfiguration : IEntityTypeConfiguration<PersonalQuest>
{
    public void Configure(EntityTypeBuilder<PersonalQuest> b)
    {
        b.HasKey(e => e.Id);
        b.Property(e => e.Name).IsRequired().HasMaxLength(200);
        b.HasIndex(e => e.Name).IsUnique();
        b.Property(e => e.Description).HasMaxLength(500);
        b.Property(e => e.Difficulty).HasConversion<string>().HasMaxLength(20);
    }
}
```

### Task 2.2: Reward / join / assignment configurations

**Files:** one file each under `Data/Configurations/`.

**Step 1:** Write:

```csharp
// PersonalQuestSkillRewardConfiguration.cs
public class PersonalQuestSkillRewardConfiguration : IEntityTypeConfiguration<PersonalQuestSkillReward>
{
    public void Configure(EntityTypeBuilder<PersonalQuestSkillReward> b)
    {
        b.HasKey(e => new { e.PersonalQuestId, e.SkillId });
        b.HasOne(e => e.PersonalQuest).WithMany(q => q.SkillRewards)
            .HasForeignKey(e => e.PersonalQuestId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(e => e.Skill).WithMany()
            .HasForeignKey(e => e.SkillId).OnDelete(DeleteBehavior.Restrict);
    }
}
```

```csharp
// PersonalQuestItemRewardConfiguration.cs
public class PersonalQuestItemRewardConfiguration : IEntityTypeConfiguration<PersonalQuestItemReward>
{
    public void Configure(EntityTypeBuilder<PersonalQuestItemReward> b)
    {
        b.HasKey(e => new { e.PersonalQuestId, e.ItemId });
        b.Property(e => e.Quantity).HasDefaultValue(1);
        b.ToTable(t => t.HasCheckConstraint("CK_PQItemReward_Qty_Positive", "\"Quantity\" >= 1"));
        b.HasOne(e => e.PersonalQuest).WithMany(q => q.ItemRewards)
            .HasForeignKey(e => e.PersonalQuestId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(e => e.Item).WithMany()
            .HasForeignKey(e => e.ItemId).OnDelete(DeleteBehavior.Restrict);
    }
}
```

```csharp
// GamePersonalQuestConfiguration.cs
public class GamePersonalQuestConfiguration : IEntityTypeConfiguration<GamePersonalQuest>
{
    public void Configure(EntityTypeBuilder<GamePersonalQuest> b)
    {
        b.HasKey(e => new { e.GameId, e.PersonalQuestId });
        b.HasOne(e => e.Game).WithMany()
            .HasForeignKey(e => e.GameId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(e => e.PersonalQuest).WithMany(q => q.GameLinks)
            .HasForeignKey(e => e.PersonalQuestId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(e => e.GameId);
        b.ToTable(t =>
        {
            t.HasCheckConstraint("CK_GamePersonalQuest_XpCost_NonNegative", "\"XpCost\" >= 0");
            t.HasCheckConstraint("CK_GamePersonalQuest_PKL_Positive",
                "\"PerKingdomLimit\" IS NULL OR \"PerKingdomLimit\" >= 1");
        });
    }
}
```

```csharp
// CharacterPersonalQuestConfiguration.cs
public class CharacterPersonalQuestConfiguration : IEntityTypeConfiguration<CharacterPersonalQuest>
{
    public void Configure(EntityTypeBuilder<CharacterPersonalQuest> b)
    {
        b.HasKey(e => e.CharacterId);        // one-to-one: a character has at most 1 PQ
        b.HasOne(e => e.Character).WithMany()
            .HasForeignKey(e => e.CharacterId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(e => e.PersonalQuest).WithMany(q => q.CharacterAssignments)
            .HasForeignKey(e => e.PersonalQuestId).OnDelete(DeleteBehavior.Restrict);
    }
}
```

### Task 2.3: Register DbSets on `WorldDbContext`

**Files:**
- Modify: `src/OvcinaHra.Api/Data/WorldDbContext.cs`

**Step 1:** Add five `DbSet` properties next to the existing ones, alphabetically grouped with the Skill/Game DbSets.
```csharp
public DbSet<PersonalQuest> PersonalQuests => Set<PersonalQuest>();
public DbSet<PersonalQuestSkillReward> PersonalQuestSkillRewards => Set<PersonalQuestSkillReward>();
public DbSet<PersonalQuestItemReward> PersonalQuestItemRewards => Set<PersonalQuestItemReward>();
public DbSet<GamePersonalQuest> GamePersonalQuests => Set<GamePersonalQuest>();
public DbSet<CharacterPersonalQuest> CharacterPersonalQuests => Set<CharacterPersonalQuest>();
```

**Step 2:** Build (just the API project) to confirm the configs compile + register.
```bash
dotnet build src/OvcinaHra.Api/OvcinaHra.Api.csproj -c Release --nologo -v:q
```
Expected: green.

**Step 3:** Commit.
```bash
git add src/OvcinaHra.Api/Data/Configurations/PersonalQuest*.cs \
        src/OvcinaHra.Api/Data/Configurations/GamePersonalQuest*.cs \
        src/OvcinaHra.Api/Data/Configurations/CharacterPersonalQuest*.cs \
        src/OvcinaHra.Api/Data/WorldDbContext.cs
git commit -m "feat(pq): EF Core configurations + DbSet registrations"
```

---

## Phase 3 — Migration

### Task 3.1: Generate the `AddPersonalQuestDomain` migration

**Files:**
- Create (auto-generated): `src/OvcinaHra.Api/Migrations/YYYYMMDD_AddPersonalQuestDomain*.cs`
- Modify (auto-generated): `src/OvcinaHra.Api/Migrations/WorldDbContextModelSnapshot.cs`

**Step 1:** Run the tool.
```bash
cd src/OvcinaHra.Api
dotnet ef migrations add AddPersonalQuestDomain --project OvcinaHra.Api.csproj
cd ../..
```
Expected: two new files + updated snapshot.

**Step 2:** Open the generated `*_AddPersonalQuestDomain.cs` and verify the `Up()` body:
- Creates `PersonalQuests`, `PersonalQuestSkillRewards`, `PersonalQuestItemRewards`, `GamePersonalQuests`, `CharacterPersonalQuests`
- Adds the 3 CHECK constraints (XpCost, PerKingdomLimit, Quantity)
- Adds unique index on `PersonalQuests(Name)`
- **No modifications to existing tables** — it's purely additive

**Step 3:** Build full solution so the generated code compiles.
```bash
cd C:/Users/TomášPajonk/source/repos/timurlain/ovcinahra
dotnet build OvcinaHra.slnx -c Release --nologo -v:q
```
Expected: green.

**Step 4:** Commit.
```bash
git add src/OvcinaHra.Api/Migrations/
git commit -m "feat(pq): EF migration AddPersonalQuestDomain (additive)"
```

---

## Phase 4 — DTOs

All DTOs go into `src/OvcinaHra.Shared/Dtos/PersonalQuestDtos.cs` (one file, per project convention — cf. `ItemDtos.cs`).

### Task 4.1: Write the DTO file

**Files:**
- Create: `src/OvcinaHra.Shared/Dtos/PersonalQuestDtos.cs`

**Step 1:** Write all DTOs in one go (record, computed `[JsonIgnore]` props, patterns lifted from `ItemDtos.cs` + `GameItemListDto`):

```csharp
using System.Text.Json.Serialization;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Extensions;

namespace OvcinaHra.Shared.Dtos;

public record PersonalQuestListDto(
    int Id,
    string Name,
    string? Description,
    TreasureQuestDifficulty Difficulty,
    bool AllowWarrior,
    bool AllowArcher,
    bool AllowMage,
    bool AllowThief,
    string? QuestCardText,
    string? RewardCardText,
    string? RewardNote,
    string? Notes,
    string? ImagePath,
    IReadOnlyList<int> SkillRewardIds,
    IReadOnlyList<PersonalQuestItemRewardSummary> ItemRewards)
{
    [JsonIgnore]
    public string DifficultyDisplay => Difficulty.GetDisplayName();

    [JsonIgnore]
    public string ClassRestrictionDisplay
    {
        get
        {
            if (!AllowWarrior && !AllowArcher && !AllowMage && !AllowThief)
                return "Všechna povolání";
            var parts = new List<string>();
            if (AllowWarrior) parts.Add(PlayerClass.Warrior.GetDisplayName());
            if (AllowArcher) parts.Add(PlayerClass.Archer.GetDisplayName());
            if (AllowMage) parts.Add(PlayerClass.Mage.GetDisplayName());
            if (AllowThief) parts.Add(PlayerClass.Thief.GetDisplayName());
            return string.Join(", ", parts);
        }
    }

    [JsonIgnore]
    public bool HasImage => !string.IsNullOrEmpty(ImagePath);
}

public record PersonalQuestItemRewardSummary(int ItemId, string ItemName, int Quantity);

public record PersonalQuestDetailDto(
    int Id,
    string Name,
    string? Description,
    TreasureQuestDifficulty Difficulty,
    bool AllowWarrior,
    bool AllowArcher,
    bool AllowMage,
    bool AllowThief,
    string? QuestCardText,
    string? RewardCardText,
    string? RewardNote,
    string? Notes,
    string? ImagePath,
    List<SkillRewardDto> SkillRewards,
    List<ItemRewardDto> ItemRewards);

public record SkillRewardDto(int SkillId, string SkillName);
public record ItemRewardDto(int ItemId, string ItemName, int Quantity);

public record CreatePersonalQuestDto(
    string Name,
    TreasureQuestDifficulty Difficulty,
    string? Description = null,
    bool AllowWarrior = false,
    bool AllowArcher = false,
    bool AllowMage = false,
    bool AllowThief = false,
    string? QuestCardText = null,
    string? RewardCardText = null,
    string? RewardNote = null,
    string? Notes = null);

public record UpdatePersonalQuestDto(
    string Name,
    TreasureQuestDifficulty Difficulty,
    string? Description,
    bool AllowWarrior,
    bool AllowArcher,
    bool AllowMage,
    bool AllowThief,
    string? QuestCardText,
    string? RewardCardText,
    string? RewardNote,
    string? Notes);

public record GamePersonalQuestListDto(
    int Id,            // PersonalQuest.Id, matches the ListDto for popup reuse
    string Name,
    string? Description,
    TreasureQuestDifficulty Difficulty,
    bool AllowWarrior,
    bool AllowArcher,
    bool AllowMage,
    bool AllowThief,
    string? QuestCardText,
    string? RewardCardText,
    string? RewardNote,
    string? Notes,
    string? ImagePath,
    int GameId,
    int XpCost,
    int? PerKingdomLimit,
    string? RewardSummary)
{
    [JsonIgnore]
    public string DifficultyDisplay => Difficulty.GetDisplayName();

    [JsonIgnore]
    public string ClassRestrictionDisplay
    {
        get
        {
            if (!AllowWarrior && !AllowArcher && !AllowMage && !AllowThief)
                return "Všechna povolání";
            var parts = new List<string>();
            if (AllowWarrior) parts.Add(PlayerClass.Warrior.GetDisplayName());
            if (AllowArcher) parts.Add(PlayerClass.Archer.GetDisplayName());
            if (AllowMage) parts.Add(PlayerClass.Mage.GetDisplayName());
            if (AllowThief) parts.Add(PlayerClass.Thief.GetDisplayName());
            return string.Join(", ", parts);
        }
    }

    [JsonIgnore]
    public bool HasImage => !string.IsNullOrEmpty(ImagePath);
}

public record CreateGamePersonalQuestDto(int GameId, int PersonalQuestId,
    int XpCost = 0, int? PerKingdomLimit = null);

public record UpdateGamePersonalQuestDto(int XpCost, int? PerKingdomLimit);

public record AddSkillRewardDto(int SkillId);
public record AddItemRewardDto(int ItemId, int Quantity = 1);
```

**Step 2:** Build.
```bash
dotnet build src/OvcinaHra.Shared/OvcinaHra.Shared.csproj -c Release --nologo -v:q
```
Expected: green.

**Step 3:** Commit.
```bash
git add src/OvcinaHra.Shared/Dtos/PersonalQuestDtos.cs
git commit -m "feat(pq): DTOs (List/Detail/Create/Update + per-game + reward)"
```

---

## Phase 5 — Endpoints (TDD)

Endpoints live in `src/OvcinaHra.Api/Endpoints/PersonalQuestEndpoints.cs`; tests in `tests/OvcinaHra.Api.Tests/Endpoints/PersonalQuestEndpointTests.cs`.

Follow the existing `ItemEndpoints.cs` / `ItemEndpointTests.cs` pattern. Register the group in `Program.cs` via `routes.MapPersonalQuestEndpoints()` alongside `MapItemEndpoints()`.

### Task 5.1: Scaffold + first failing test (list-empty)

**Files:**
- Create: `src/OvcinaHra.Api/Endpoints/PersonalQuestEndpoints.cs`
- Create: `tests/OvcinaHra.Api.Tests/Endpoints/PersonalQuestEndpointTests.cs`
- Modify: `src/OvcinaHra.Api/Program.cs` — add `routes.MapPersonalQuestEndpoints();`

**Step 1:** Write the endpoint scaffold with just the group + `GetAll`.
```csharp
public static class PersonalQuestEndpoints
{
    public static RouteGroupBuilder MapPersonalQuestEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/personal-quests").WithTags("PersonalQuests");
        group.MapGet("/", GetAll);
        return group;
    }

    private static async Task<Ok<List<PersonalQuestListDto>>> GetAll(WorldDbContext db)
    {
        var quests = await db.PersonalQuests
            .AsNoTracking()
            .Include(q => q.SkillRewards)
            .Include(q => q.ItemRewards).ThenInclude(r => r.Item)
            .OrderBy(q => q.Name)
            .ToListAsync();

        return TypedResults.Ok(quests.Select(ToListDto).ToList());
    }

    private static PersonalQuestListDto ToListDto(PersonalQuest q) => new(
        q.Id, q.Name, q.Description, q.Difficulty,
        q.AllowWarrior, q.AllowArcher, q.AllowMage, q.AllowThief,
        q.QuestCardText, q.RewardCardText, q.RewardNote, q.Notes, q.ImagePath,
        q.SkillRewards.Select(sr => sr.SkillId).ToList(),
        q.ItemRewards.Select(ir => new PersonalQuestItemRewardSummary(ir.ItemId, ir.Item.Name, ir.Quantity)).ToList());
}
```

**Step 2:** Write first integration test — mirror `ItemEndpointTests` / `SkillEndpointTests`, use the existing `WebApplicationFixture`.

```csharp
public class PersonalQuestEndpointTests(WebApplicationFixture fx) : IClassFixture<WebApplicationFixture>
{
    [Fact]
    public async Task GetAll_Empty_ReturnsEmptyList()
    {
        var client = fx.CreateClient();
        var response = await client.GetFromJsonAsync<List<PersonalQuestListDto>>("/api/personal-quests");
        Assert.NotNull(response);
        Assert.Empty(response);
    }
}
```

**Step 3:** Run the test.
```bash
dotnet test tests/OvcinaHra.Api.Tests -c Release --nologo \
  --filter "FullyQualifiedName~PersonalQuestEndpointTests.GetAll_Empty"
```
Expected: PASS (DB is empty on a fresh container).

**Step 4:** Commit.
```bash
git add src/OvcinaHra.Api/Endpoints/PersonalQuestEndpoints.cs \
        src/OvcinaHra.Api/Program.cs \
        tests/OvcinaHra.Api.Tests/Endpoints/PersonalQuestEndpointTests.cs
git commit -m "feat(pq): GET /api/personal-quests (empty-list smoke test)"
```

### Task 5.2: POST + GET-by-id + DELETE (catalog CRUD)

Follow the same TDD beat for each method:

1. Write failing test (e.g. `Create_WithValidDto_ReturnsCreatedWithId`, `GetById_NotFound_Returns404`, `Delete_Cascades_RemovesRewards`).
2. Run — expect red.
3. Implement the endpoint method in `PersonalQuestEndpoints.cs`.
4. Run — expect green.
5. Commit each endpoint method + its test as a single commit, e.g. `test(pq): failing Create test` then `feat(pq): POST /api/personal-quests`.

**Endpoint signatures to add:**
```csharp
group.MapGet("/{id:int}", GetById);
group.MapPost("/", Create);
group.MapPut("/{id:int}", Update);
group.MapDelete("/{id:int}", Delete);
```

Look at `ItemEndpoints.cs` for the `ToDetailDto` / `FindAsync` / `db.Items.Remove` shape.

**Important:** `GetById` must include `SkillRewards.Skill` + `ItemRewards.Item` to populate the DetailDto's friendly names.

### Task 5.3: Per-game endpoints + `RewardSummary` computation

**Endpoint signatures:**
```csharp
group.MapGet("/by-game/{gameId:int}", GetByGame);
group.MapPost("/game-link", CreateGameLink);
group.MapPut("/game-link/{gameId:int}/{pqId:int}", UpdateGameLink);
group.MapDelete("/game-link/{gameId:int}/{pqId:int}", DeleteGameLink);
```

**`GetByGame`** — joins GamePersonalQuests with PersonalQuests + Skills + Items to build `GamePersonalQuestListDto` with the computed `RewardSummary`. Mirrors `ItemEndpoints.GetByGame` (which joins crafting recipes).

```csharp
private static async Task<Ok<List<GamePersonalQuestListDto>>> GetByGame(int gameId, WorldDbContext db)
{
    var gpqs = await db.GamePersonalQuests
        .AsNoTracking()
        .Where(g => g.GameId == gameId)
        .Include(g => g.PersonalQuest).ThenInclude(q => q.SkillRewards).ThenInclude(sr => sr.Skill)
        .Include(g => g.PersonalQuest).ThenInclude(q => q.ItemRewards).ThenInclude(ir => ir.Item)
        .OrderBy(g => g.PersonalQuest.Name)
        .ToListAsync();

    return TypedResults.Ok(gpqs.Select(ToGameListDto).ToList());
}

private static GamePersonalQuestListDto ToGameListDto(GamePersonalQuest g)
{
    var q = g.PersonalQuest;
    return new(
        q.Id, q.Name, q.Description, q.Difficulty,
        q.AllowWarrior, q.AllowArcher, q.AllowMage, q.AllowThief,
        q.QuestCardText, q.RewardCardText, q.RewardNote, q.Notes, q.ImagePath,
        g.GameId, g.XpCost, g.PerKingdomLimit,
        BuildRewardSummary(q));
}

private static string? BuildRewardSummary(PersonalQuest q)
{
    var parts = new List<string>();
    if (q.SkillRewards.Count > 0)
        parts.Add(string.Join(", ", q.SkillRewards.OrderBy(s => s.Skill.Name).Select(s => s.Skill.Name)));
    if (q.ItemRewards.Count > 0)
        parts.Add(string.Join(", ",
            q.ItemRewards.OrderBy(i => i.Item.Name).Select(i => $"{i.Item.Name} ×{i.Quantity}")));
    return parts.Count > 0 ? string.Join(" │ ", parts) : null;
}
```

**Tests — one beat per endpoint:**
- `GetByGame_Empty_Returns404OrEmpty` (choose one and stick to it — mirror the items tests)
- `CreateGameLink_Persists_Returns201`
- `UpdateGameLink_TunesXpCost`
- `DeleteGameLink_RemovesConfig_KeepsCatalogEntry`
- `GetByGame_WithRewards_BuildsSummary` — the most valuable test; asserts the string format `"Druid │ Léčivá dlaň ×1"`

Commit each TDD beat.

### Task 5.4: Reward add/remove endpoints

**Endpoint signatures:**
```csharp
group.MapPost("/{id:int}/skill-rewards", AddSkillReward);
group.MapDelete("/{id:int}/skill-rewards/{skillId:int}", RemoveSkillReward);
group.MapPost("/{id:int}/item-rewards", AddItemReward);
group.MapDelete("/{id:int}/item-rewards/{itemId:int}", RemoveItemReward);
```

**Tests** (one per verb):
- `AddSkillReward_Persists_Returns201_AndSecondPostIsConflict`
- `AddItemReward_StoresQuantity`
- `RemoveSkillReward_Idempotent_NoContentWhenAlreadyGone`

Commit each beat.

### Task 5.5: Full test suite green

**Step 1:** Run the whole test suite end-to-end.
```bash
dotnet test tests/OvcinaHra.Api.Tests -c Release --nologo
```
Expected: all green. Expect 10–15 new PersonalQuest tests + existing 176+ still passing.

**Step 2:** If anything is red, fix the cause (do NOT skip tests) before moving on.

---

## Phase 6 — Client

### Task 6.1: `PersonalQuestService` API wrapper

**Files:**
- Create: `src/OvcinaHra.Client/Services/PersonalQuestService.cs`
- Modify: `src/OvcinaHra.Client/Program.cs` — `builder.Services.AddScoped<PersonalQuestService>();`

**Step 1:** Wrap all endpoints. Mirror `SkillService.cs` — simple `ApiClient` delegation, no state. Example:
```csharp
public class PersonalQuestService(ApiClient api)
{
    public Task<List<PersonalQuestListDto>> GetAllAsync() => api.GetListAsync<PersonalQuestListDto>("/api/personal-quests");
    public Task<PersonalQuestDetailDto?> GetByIdAsync(int id) => api.GetAsync<PersonalQuestDetailDto>($"/api/personal-quests/{id}");
    // ...etc
}
```

**Step 2:** Build Client.
```bash
dotnet build src/OvcinaHra.Client/OvcinaHra.Client.csproj -c Release --nologo -v:q
```

**Step 3:** Commit.
```bash
git add src/OvcinaHra.Client/Services/PersonalQuestService.cs src/OvcinaHra.Client/Program.cs
git commit -m "feat(pq): client service wrapper + DI registration"
```

### Task 6.2: Nav entries in both sections

**Files:**
- Modify: `src/OvcinaHra.Client/Layout/NavMenu.razor`

**Step 1:** Add the Hra entry right after the Questy entry:
```razor
<div class="nav-item px-3">
    <NavLink class="nav-link" href="personal-quests">
        <i class="bi bi-person-workspace"></i><span class="nav-label">Osobní questy</span>
    </NavLink>
</div>
```

**Step 2:** Add the identical entry in **Katalog světa** right after `quests?catalog=true`, using `href="personal-quests?catalog=true"` and the same icon / label — matches design § *Nav*.

**Step 3:** Commit.
```bash
git add src/OvcinaHra.Client/Layout/NavMenu.razor
git commit -m "feat(pq): add Osobní questy nav entries (Hra + Katalog světa)"
```

### Task 6.3: `PersonalQuestList.razor` — skeleton + catalog grid

**Files:**
- Create: `src/OvcinaHra.Client/Pages/PersonalQuests/PersonalQuestList.razor`

**Step 1:** Scaffold the page modelled exactly on `Pages/Items/ItemList.razor` (post PR #32). Top-level:
- `@page "/personal-quests"`, `[SupplyParameterFromQuery] public bool Catalog { get; set; }`
- Czech header "Katalog osobních questů" / "Osobní questy" — mirror the Items header
- "Jen tato hra" / "Katalog" button toggle
- "+ Nový osobní quest" button → `OpenModal((PersonalQuestListDto?)null)`
- `@if (loading)`, `else if (!Catalog && GameContext.SelectedGameId is null)`, `else if (!Catalog && gameItems.Count == 0)`, `else if (Catalog) { /* catalog grid */ }`, `else { /* game grid + context menu */ }`

**Step 2:** Implement ONLY the catalog grid first — column list per design § *UI → Two grid views*. LayoutKey `grid-layout-personal-quests-catalog`. Use the same visible/hidden default scheme.

**Step 3:** Wire `LoadAsync` to populate `catalogItems` via `PersonalQuestSvc.GetAllAsync()`.

**Step 4:** Run `devStart.bat` and visually spot-check `/personal-quests?catalog=true` loads (empty). Hit `+ Nový` — popup should open (even if form is a stub).

**Step 5:** Commit `feat(pq): catalog grid skeleton` — WIP popup OK.

### Task 6.4: Detail popup form + rewards pickers

**Step 1:** Flesh the popup. `DxFormLayout` with all catalog fields per design § *Detail popup*. Use `DxComboBox` for `Difficulty`, 4 `DxCheckBox` for class matrix, `DxTextBox`/`DxMemo` for text fields, `ImagePicker` for the image.

**Step 2:** Below the form layout, add the **Rewards card** (only when `editingId.HasValue`):
- Skills: list of chips (name + `×` remove button), `DxComboBox` with all `SkillListDto`s except already-added + `Přidat` button → calls `PersonalQuestSvc.AddSkillRewardAsync(id, skillId)`
- Items: same pattern with `DxSpinEdit` for quantity

**Step 3:** `Save` button → POST on new, PUT on existing. On success: close + reload.

**Step 4:** `Delete` button + confirm popup (mirror Items page exactly).

**Step 5:** Test end-to-end locally: create quest, attach 1 skill + 1 item, save, reopen, verify state round-trips.

**Step 6:** Commit `feat(pq): detail popup with rewards pickers`.

### Task 6.5: Game-view grid + context menu + per-game card

**Step 1:** Game grid: column list per design + three-dots fixed-left column + `DxContextMenu` with **Upravit / Odebrat z hry / Smazat** items. Mirror the post-PR-#32 Items game grid exactly. LayoutKey `grid-layout-personal-quests-game`.

**Step 2:** In the popup (only when `!Catalog && SelectedGameId.HasValue && editingId.HasValue`), render the **Per-game card**:
- `DxSpinEdit` for `XpCost` (MinValue 0)
- `DxSpinEdit` for `PerKingdomLimit` (MinValue 1, `NullText="(neomezeno)"`)
- "Přidat do hry" button (creates GPQ) OR "Uložit nastavení" + "Odebrat z hry" buttons (updates/deletes GPQ)

**Step 3:** `LoadAsync` in game mode calls `PersonalQuestSvc.GetByGameAsync(gid)` and populates `gameItems: List<GamePersonalQuestListDto>`. Catalog-mode catalog/assignment works identically to the `ItemList` pattern.

**Step 4:** Test locally: assign a quest to the current game, tune XpCost, verify grid updates, open context menu, unassign, reassign.

**Step 5:** Commit `feat(pq): game-view grid + context menu + per-game card`.

### Task 6.6: `ToolSearch`-style catalog `Přidat/Odebrat` column

Only needed if Catalog view should be able to opt quests in/out of the current game — yes per design. Column definition lifted straight from `ItemList.razor`. Wire `AssignAsync` / `UnassignAsync` → `CreateGameLinkAsync(gid, id, xpCost=0, pkl=null)` / `DeleteGameLinkAsync(gid, id)`.

Commit `feat(pq): catalog Přidat/Odebrat column`.

---

## Phase 7 — Finalize

### Task 7.1: Version bump + final solution build

**Files:**
- Modify: `src/OvcinaHra.Client/OvcinaHra.Client.csproj` — `<Version>0.5.3</Version>` → `<Version>0.6.0</Version>`.

**Step 1:** Edit the csproj.

**Step 2:** Full solution build.
```bash
dotnet build OvcinaHra.slnx -c Release --nologo -v:q
```
Expected: green, 0 warnings.

**Step 3:** Full test suite one more time.
```bash
dotnet test tests/OvcinaHra.Api.Tests -c Release --nologo
```
Expected: green.

**Step 4:** Commit.
```bash
git add src/OvcinaHra.Client/OvcinaHra.Client.csproj
git commit -m "chore(pq): version bump 0.5.3 -> 0.6.0"
```

### Task 7.2: Push + PR

**Step 1:** Push the branch.
```bash
git push -u origin feat/personal-quest-domain
```

**Step 2:** Open the PR.
```bash
gh pr create --title "feat: PersonalQuest domain (catalog + per-game + character assignment) [v0.6.0]" \
  --body "$(cat <<'EOF'
## Summary

First shipping cut of the PersonalQuest domain — world-level catalog of per-character mini-quests, per-game config, and a character-assignment table ready for a follow-up UI. Design doc: `docs/plans/2026-04-19-personal-quest-domain-design.md`.

## Server

- 4 new entities + 1 link: `PersonalQuest`, `PersonalQuestSkillReward`, `PersonalQuestItemReward`, `GamePersonalQuest`, `CharacterPersonalQuest`.
- EF migration `AddPersonalQuestDomain` — additive only, no modifications to existing tables. Auto-applied at API startup via `db.Database.MigrateAsync()`.
- Endpoints under `/api/personal-quests` (CRUD + per-game link + reward add/remove).
- Testcontainers-PostgreSQL integration tests for every endpoint.

## Client

- `/personal-quests` page with catalog/game grid split (same UX as `/items`).
- Nav entries added in Hra and Katalog světa at matching positions.
- Detail popup: form + rewards pickers (skills + items with quantity) + per-game config card.
- Server-computed `RewardSummary` string for at-a-glance game-view rows.

## Deferred (explicit)

- Character-assignment UI (data ships; form/page follows).
- Completion-tracking toggle.
- Card printing.
- Kingdom/Merchant entities.

## Test plan

- [ ] CI passes (API CI + Client CI)
- [ ] Manually: create a "Druid" quest in the catalog, attach Dovednost Druid (skill) + Léčivá dlaň (item), add to game 30, verify RewardSummary renders
- [ ] Switch Katalog ↔ Jen tato hra — both grids work, LayoutKeys persist independently
- [ ] Context menu on game view: Upravit / Odebrat z hry / Smazat all work
- [ ] Nav: Dovednosti entries visible in both Hra and Katalog světa
- [ ] Migration applies cleanly against prod on deploy

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

**Step 3:** Watch CI; address Copilot comments; merge when green.

---

## Cross-cutting reminders

- **Czech UI / English code** — page routes, identifiers, method names all English; every user-facing string Czech with proper diacritics. Keep `PlayerClass` display names stable.
- **LayoutKey bump rule** — if you change grid columns later, increment the key suffix per `feedback_ovcinagrid_layoutkey_bump`.
- **No N+1 queries** — `Include().ThenInclude()` for all reward navigation reads.
- **Guard tests** — every endpoint gets at least a happy-path test; 404 and conflict cases for mutations.
- **Don't commit without explicit approval** when working with subagents — per `feedback_no_commit_without_approval`.

---

## Execution handoff

Plan complete and saved to `docs/plans/2026-04-19-personal-quest-domain-implementation.md`.

Two execution options:

1. **Subagent-Driven (this session)** — I dispatch fresh subagents per task, review between tasks, fast iteration.
2. **Parallel Session (separate)** — Open a new session in the branch, use `superpowers:executing-plans` to batch through with checkpoints.

Which approach?
