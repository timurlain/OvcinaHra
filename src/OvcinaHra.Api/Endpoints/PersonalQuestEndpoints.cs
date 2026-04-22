using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OvcinaHra.Api.Data;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Endpoints;

public static class PersonalQuestEndpoints
{
    public static RouteGroupBuilder MapPersonalQuestEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/personal-quests").WithTags("PersonalQuests");

        group.MapGet("/", GetAll);
        group.MapGet("/{id:int}", GetById);
        group.MapPost("/", Create);
        group.MapPut("/{id:int}", Update);
        group.MapDelete("/{id:int}", Delete);

        // Per-game link endpoints
        group.MapGet("/by-game/{gameId:int}", GetByGame);
        group.MapPost("/game-link", CreateGameLink);
        group.MapPut("/game-link/{gameId:int}/{pqId:int}", UpdateGameLink);
        group.MapDelete("/game-link/{gameId:int}/{pqId:int}", DeleteGameLink);

        // Reward link endpoints
        group.MapPost("/{id:int}/skill-rewards", AddSkillReward);
        group.MapDelete("/{id:int}/skill-rewards/{skillId:int}", RemoveSkillReward);
        group.MapPost("/{id:int}/item-rewards", AddItemReward);
        group.MapDelete("/{id:int}/item-rewards/{itemId:int}", RemoveItemReward);

        return group;
    }

    private static async Task<Ok<List<PersonalQuestListDto>>> GetAll(WorldDbContext db)
    {
        var quests = await db.PersonalQuests
            .AsNoTracking()
            .Include(q => q.SkillRewards).ThenInclude(sr => sr.Skill)
            .Include(q => q.ItemRewards).ThenInclude(r => r.Item)
            .OrderBy(q => q.Name)
            .ToListAsync();

        return TypedResults.Ok(quests.Select(ToListDto).ToList());
    }

    private static async Task<Results<Ok<PersonalQuestDetailDto>, NotFound>> GetById(int id, WorldDbContext db)
    {
        var q = await db.PersonalQuests
            .AsNoTracking()
            .Include(pq => pq.SkillRewards).ThenInclude(sr => sr.Skill)
            .Include(pq => pq.ItemRewards).ThenInclude(ir => ir.Item)
            .FirstOrDefaultAsync(pq => pq.Id == id);
        if (q is null) return TypedResults.NotFound();

        return TypedResults.Ok(ToDetailDto(q));
    }

    private static async Task<Created<PersonalQuestDetailDto>> Create(CreatePersonalQuestDto dto, WorldDbContext db)
    {
        var q = new PersonalQuest
        {
            Name = dto.Name,
            Difficulty = dto.Difficulty,
            Description = dto.Description,
            AllowWarrior = dto.AllowWarrior,
            AllowArcher = dto.AllowArcher,
            AllowMage = dto.AllowMage,
            AllowThief = dto.AllowThief,
            QuestCardText = dto.QuestCardText,
            RewardCardText = dto.RewardCardText,
            RewardNote = dto.RewardNote,
            Notes = dto.Notes,
            XpCost = dto.XpCost
        };
        db.PersonalQuests.Add(q);
        await db.SaveChangesAsync();

        return TypedResults.Created($"/api/personal-quests/{q.Id}", ToDetailDto(q));
    }

    private static async Task<Results<NoContent, NotFound>> Update(int id, UpdatePersonalQuestDto dto, WorldDbContext db)
    {
        var q = await db.PersonalQuests.FindAsync(id);
        if (q is null) return TypedResults.NotFound();

        q.Name = dto.Name;
        q.Difficulty = dto.Difficulty;
        q.Description = dto.Description;
        q.AllowWarrior = dto.AllowWarrior;
        q.AllowArcher = dto.AllowArcher;
        q.AllowMage = dto.AllowMage;
        q.AllowThief = dto.AllowThief;
        q.QuestCardText = dto.QuestCardText;
        q.RewardCardText = dto.RewardCardText;
        q.RewardNote = dto.RewardNote;
        q.Notes = dto.Notes;
        q.XpCost = dto.XpCost;

        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound>> Delete(int id, WorldDbContext db)
    {
        var q = await db.PersonalQuests.FindAsync(id);
        if (q is null) return TypedResults.NotFound();

        db.PersonalQuests.Remove(q);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static PersonalQuestDetailDto ToDetailDto(PersonalQuest q) => new(
        q.Id, q.Name, q.Description, q.Difficulty,
        q.AllowWarrior, q.AllowArcher, q.AllowMage, q.AllowThief,
        q.QuestCardText, q.RewardCardText, q.RewardNote, q.Notes, q.ImagePath,
        q.SkillRewards.Select(sr => new SkillRewardDto(sr.SkillId, sr.Skill?.Name ?? string.Empty)).ToList(),
        q.ItemRewards.Select(ir => new ItemRewardDto(ir.ItemId, ir.Item?.Name ?? string.Empty, ir.Quantity)).ToList(),
        q.XpCost);

    private static PersonalQuestListDto ToListDto(PersonalQuest q) => new(
        q.Id, q.Name, q.Description, q.Difficulty,
        q.AllowWarrior, q.AllowArcher, q.AllowMage, q.AllowThief,
        q.QuestCardText, q.RewardCardText, q.RewardNote, q.Notes, q.ImagePath,
        q.SkillRewards.Select(sr => sr.SkillId).ToList(),
        q.ItemRewards.Select(ir => new PersonalQuestItemRewardSummary(ir.ItemId, ir.Item.Name, ir.Quantity)).ToList(),
        BuildRewardSummary(q),
        q.XpCost);

    // ---------- Per-game link endpoints ----------

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

    private static async Task<Results<Created<GamePersonalQuestDto>, Conflict>> CreateGameLink(
        CreateGamePersonalQuestDto dto, WorldDbContext db)
    {
        var exists = await db.GamePersonalQuests
            .AnyAsync(g => g.GameId == dto.GameId && g.PersonalQuestId == dto.PersonalQuestId);
        if (exists) return TypedResults.Conflict();

        var gpq = new GamePersonalQuest
        {
            GameId = dto.GameId,
            PersonalQuestId = dto.PersonalQuestId,
            XpCost = dto.XpCost,
            PerKingdomLimit = dto.PerKingdomLimit
        };
        db.GamePersonalQuests.Add(gpq);
        await db.SaveChangesAsync();

        return TypedResults.Created(
            $"/api/personal-quests/game-link/{gpq.GameId}/{gpq.PersonalQuestId}",
            new GamePersonalQuestDto(gpq.GameId, gpq.PersonalQuestId, gpq.XpCost, gpq.PerKingdomLimit));
    }

    private static async Task<Results<NoContent, NotFound>> UpdateGameLink(
        int gameId, int pqId, UpdateGamePersonalQuestDto dto, WorldDbContext db)
    {
        var gpq = await db.GamePersonalQuests.FindAsync(gameId, pqId);
        if (gpq is null) return TypedResults.NotFound();

        gpq.XpCost = dto.XpCost;
        gpq.PerKingdomLimit = dto.PerKingdomLimit;
        await db.SaveChangesAsync();

        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound>> DeleteGameLink(
        int gameId, int pqId, WorldDbContext db)
    {
        var gpq = await db.GamePersonalQuests.FindAsync(gameId, pqId);
        if (gpq is null) return TypedResults.NotFound();

        db.GamePersonalQuests.Remove(gpq);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static GamePersonalQuestListDto ToGameListDto(GamePersonalQuest g)
    {
        var q = g.PersonalQuest;
        return new GamePersonalQuestListDto(
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
        {
            parts.Add(string.Join(", ",
                q.SkillRewards.OrderBy(s => s.Skill.Name).Select(s => s.Skill.Name)));
        }
        if (q.ItemRewards.Count > 0)
        {
            parts.Add(string.Join(", ",
                q.ItemRewards.OrderBy(i => i.Item.Name).Select(i => $"{i.Item.Name} ×{i.Quantity}")));
        }
        return parts.Count > 0 ? string.Join(" │ ", parts) : null;
    }

    // ---------- Reward link endpoints ----------

    private static async Task<Results<Created, NotFound, Conflict>> AddSkillReward(
        int id, AddSkillRewardDto dto, WorldDbContext db)
    {
        var quest = await db.PersonalQuests.FindAsync(id);
        if (quest is null) return TypedResults.NotFound();

        var exists = await db.PersonalQuestSkillRewards
            .AnyAsync(sr => sr.PersonalQuestId == id && sr.SkillId == dto.SkillId);
        if (exists) return TypedResults.Conflict();

        db.PersonalQuestSkillRewards.Add(new PersonalQuestSkillReward
        {
            PersonalQuestId = id,
            SkillId = dto.SkillId
        });
        await db.SaveChangesAsync();

        return TypedResults.Created($"/api/personal-quests/{id}/skill-rewards/{dto.SkillId}");
    }

    private static async Task<Results<NoContent, NotFound>> RemoveSkillReward(int id, int skillId, WorldDbContext db)
    {
        var sr = await db.PersonalQuestSkillRewards.FindAsync(id, skillId);
        if (sr is null) return TypedResults.NotFound();

        db.PersonalQuestSkillRewards.Remove(sr);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Results<Created, NotFound, Conflict>> AddItemReward(
        int id, AddItemRewardDto dto, WorldDbContext db)
    {
        var quest = await db.PersonalQuests.FindAsync(id);
        if (quest is null) return TypedResults.NotFound();

        var exists = await db.PersonalQuestItemRewards
            .AnyAsync(ir => ir.PersonalQuestId == id && ir.ItemId == dto.ItemId);
        if (exists) return TypedResults.Conflict();

        db.PersonalQuestItemRewards.Add(new PersonalQuestItemReward
        {
            PersonalQuestId = id,
            ItemId = dto.ItemId,
            Quantity = dto.Quantity
        });
        await db.SaveChangesAsync();

        return TypedResults.Created($"/api/personal-quests/{id}/item-rewards/{dto.ItemId}");
    }

    private static async Task<Results<NoContent, NotFound>> RemoveItemReward(int id, int itemId, WorldDbContext db)
    {
        var ir = await db.PersonalQuestItemRewards.FindAsync(id, itemId);
        if (ir is null) return TypedResults.NotFound();

        db.PersonalQuestItemRewards.Remove(ir);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }
}
