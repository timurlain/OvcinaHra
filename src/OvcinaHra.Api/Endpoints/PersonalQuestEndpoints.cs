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
