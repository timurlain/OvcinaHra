using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OvcinaHra.Api.Data;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Endpoints;

public static class SkillEndpoints
{
    public static RouteGroupBuilder MapSkillEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/skills").WithTags("Skills");

        group.MapGet("/", GetAll);
        group.MapGet("/{id:int}", GetById);
        group.MapGet("/{id:int}/usage", GetUsage);
        group.MapPost("/", Create);
        group.MapPut("/{id:int}", Update);
        group.MapDelete("/{id:int}", Delete);

        return group;
    }

    private static async Task<Ok<IReadOnlyList<SkillDto>>> GetAll(WorldDbContext db)
    {
        var skills = await db.Skills
            .Include(s => s.BuildingRequirements)
            .OrderBy(s => s.Name)
            .ToListAsync();

        // Single rollup query for the catalog "Smazat zablokováno" badge —
        // group-by on GameSkills.TemplateSkillId is bounded by template count
        // and the column is FK-indexed.
        var usageMap = await db.GameSkills
            .Where(gs => gs.TemplateSkillId != null)
            .GroupBy(gs => gs.TemplateSkillId!.Value)
            .Select(g => new { TemplateId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TemplateId, x => x.Count);

        IReadOnlyList<SkillDto> dtos = skills
            .Select(s => ToDto(s, usageMap.GetValueOrDefault(s.Id, 0)))
            .ToList();
        return TypedResults.Ok(dtos);
    }

    private static async Task<Results<Ok<SkillDto>, NotFound>> GetById(int id, WorldDbContext db)
    {
        var skill = await db.Skills
            .Include(s => s.BuildingRequirements)
            .SingleOrDefaultAsync(s => s.Id == id);
        if (skill is null) return TypedResults.NotFound();

        var usageCount = await db.GameSkills.CountAsync(gs => gs.TemplateSkillId == id);
        return TypedResults.Ok(ToDto(skill, usageCount));
    }

    /// <summary>
    /// Per-skill usage rollup. Drives the SkillDetail "V tomto roce" tab.
    /// Drift detection stays client-side per the design brief — this endpoint
    /// just answers "where are the copies?".
    /// </summary>
    private static async Task<Results<Ok<SkillUsageDto>, NotFound>> GetUsage(int id, WorldDbContext db)
    {
        var exists = await db.Skills.AnyAsync(s => s.Id == id);
        if (!exists) return TypedResults.NotFound();

        var games = await db.GameSkills
            .Where(gs => gs.TemplateSkillId == id)
            .OrderByDescending(gs => gs.Game.StartDate)
            .Select(gs => new SkillUsageGameDto(gs.GameId, gs.Id, gs.Game.Name, gs.Game.Edition))
            .ToListAsync();

        return TypedResults.Ok(new SkillUsageDto(id, games.Count, games));
    }

    private static async Task<Results<Created<SkillDto>, BadRequest<ProblemDetails>, Conflict<ProblemDetails>>> Create(
        CreateSkillRequest dto, WorldDbContext db)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            return TypedResults.BadRequest(new ProblemDetails
            {
                Title = "Název dovednosti je povinný.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var nameExists = await db.Skills.AnyAsync(s => s.Name == dto.Name);
        if (nameExists)
        {
            return TypedResults.Conflict(new ProblemDetails
            {
                Title = "Dovednost s tímto názvem již existuje.",
                Status = StatusCodes.Status409Conflict
            });
        }

        var requiredBuildingIds = dto.RequiredBuildingIds?.Distinct().ToList() ?? [];
        var buildingError = await ValidateBuildingIdsAsync(requiredBuildingIds, db);
        if (buildingError is not null) return TypedResults.BadRequest(buildingError);

        var skill = new Skill
        {
            Name = dto.Name,
            Category = dto.Category,
            ClassRestriction = dto.ClassRestriction,
            Effect = dto.Effect,
            RequirementNotes = dto.RequirementNotes,
            BuildingRequirements = requiredBuildingIds
                .Select(bid => new SkillBuildingRequirement { BuildingId = bid })
                .ToList()
        };

        db.Skills.Add(skill);
        await db.SaveChangesAsync();

        return TypedResults.Created($"/api/skills/{skill.Id}", ToDto(skill));
    }

    private static async Task<Results<NoContent, NotFound, BadRequest<ProblemDetails>, Conflict<ProblemDetails>>> Update(
        int id, UpdateSkillRequest dto, WorldDbContext db)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            return TypedResults.BadRequest(new ProblemDetails
            {
                Title = "Název dovednosti je povinný.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var skill = await db.Skills
            .Include(s => s.BuildingRequirements)
            .SingleOrDefaultAsync(s => s.Id == id);
        if (skill is null) return TypedResults.NotFound();

        var nameConflict = await db.Skills.AnyAsync(s => s.Id != id && s.Name == dto.Name);
        if (nameConflict)
        {
            return TypedResults.Conflict(new ProblemDetails
            {
                Title = "Dovednost s tímto názvem již existuje.",
                Status = StatusCodes.Status409Conflict
            });
        }

        var requiredBuildingIds = dto.RequiredBuildingIds?.Distinct().ToList() ?? [];
        var buildingError = await ValidateBuildingIdsAsync(requiredBuildingIds, db);
        if (buildingError is not null) return TypedResults.BadRequest(buildingError);

        skill.Name = dto.Name;
        skill.Category = dto.Category;
        skill.ClassRestriction = dto.ClassRestriction;
        skill.Effect = dto.Effect;
        skill.RequirementNotes = dto.RequirementNotes;

        // Replace BuildingRequirements as a set
        var currentIds = skill.BuildingRequirements.Select(r => r.BuildingId).ToHashSet();
        var desiredIds = requiredBuildingIds.ToHashSet();

        foreach (var req in skill.BuildingRequirements.Where(r => !desiredIds.Contains(r.BuildingId)).ToList())
        {
            skill.BuildingRequirements.Remove(req);
        }
        foreach (var bid in desiredIds.Where(b => !currentIds.Contains(b)))
        {
            skill.BuildingRequirements.Add(new SkillBuildingRequirement { SkillId = id, BuildingId = bid });
        }

        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound, Conflict<ProblemDetails>>> Delete(
        int id, WorldDbContext db)
    {
        // Atomic check-and-delete via a single DELETE … WHERE NOT EXISTS so a
        // concurrent INSERT into GameSkills (between count and delete) cannot
        // sneak past the "Smazat zablokováno" rule. ExecuteDeleteAsync runs
        // server-side as one statement; rows-affected disambiguates 404 vs
        // 409 below.
        var deleted = await db.Skills
            .Where(s => s.Id == id
                && !db.GameSkills.Any(gs => gs.TemplateSkillId == id))
            .ExecuteDeleteAsync();

        if (deleted > 0) return TypedResults.NoContent();

        // Either the skill never existed OR it had copies. Disambiguate.
        var stillExists = await db.Skills.AnyAsync(s => s.Id == id);
        if (!stillExists) return TypedResults.NotFound();

        var copyCount = await db.GameSkills.CountAsync(gs => gs.TemplateSkillId == id);
        return TypedResults.Conflict(new ProblemDetails
        {
            Title = "Dovednost nelze smazat",
            Detail = $"Šablona má kopie v {copyCount} hrách. Před smazáním je všechny odeber.",
            Status = StatusCodes.Status409Conflict
        });
    }

    private static async Task<ProblemDetails?> ValidateBuildingIdsAsync(
        IReadOnlyCollection<int> requiredBuildingIds, WorldDbContext db)
    {
        if (requiredBuildingIds.Count == 0) return null;

        var knownBuildingCount = await db.Buildings
            .CountAsync(b => requiredBuildingIds.Contains(b.Id));
        if (knownBuildingCount != requiredBuildingIds.Count)
        {
            return new ProblemDetails
            {
                Title = "Některé z požadovaných budov neexistují.",
                Status = StatusCodes.Status400BadRequest
            };
        }
        return null;
    }

    private static SkillDto ToDto(Skill skill, int usageCount = 0) => new(
        skill.Id,
        skill.Name,
        skill.Category,
        skill.ClassRestriction,
        skill.Effect,
        skill.RequirementNotes,
        skill.ImagePath,
        skill.BuildingRequirements.Select(r => r.BuildingId).ToList(),
        UsageCount: usageCount);
}
