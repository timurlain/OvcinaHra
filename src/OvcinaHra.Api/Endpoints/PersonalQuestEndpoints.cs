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
            Notes = dto.Notes
        };
        db.PersonalQuests.Add(q);
        await db.SaveChangesAsync();

        return TypedResults.Created($"/api/personal-quests/{q.Id}", ToDetailDto(q));
    }

    private static PersonalQuestDetailDto ToDetailDto(PersonalQuest q) => new(
        q.Id, q.Name, q.Description, q.Difficulty,
        q.AllowWarrior, q.AllowArcher, q.AllowMage, q.AllowThief,
        q.QuestCardText, q.RewardCardText, q.RewardNote, q.Notes, q.ImagePath,
        q.SkillRewards.Select(sr => new SkillRewardDto(sr.SkillId, sr.Skill?.Name ?? string.Empty)).ToList(),
        q.ItemRewards.Select(ir => new ItemRewardDto(ir.ItemId, ir.Item?.Name ?? string.Empty, ir.Quantity)).ToList());

    private static PersonalQuestListDto ToListDto(PersonalQuest q) => new(
        q.Id, q.Name, q.Description, q.Difficulty,
        q.AllowWarrior, q.AllowArcher, q.AllowMage, q.AllowThief,
        q.QuestCardText, q.RewardCardText, q.RewardNote, q.Notes, q.ImagePath,
        q.SkillRewards.Select(sr => sr.SkillId).ToList(),
        q.ItemRewards.Select(ir => new PersonalQuestItemRewardSummary(ir.ItemId, ir.Item.Name, ir.Quantity)).ToList());
}
