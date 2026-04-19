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

        IReadOnlyList<SkillDto> dtos = skills.Select(ToDto).ToList();
        return TypedResults.Ok(dtos);
    }

    private static async Task<Results<Ok<SkillDto>, NotFound>> GetById(int id, WorldDbContext db)
    {
        var skill = await db.Skills
            .Include(s => s.BuildingRequirements)
            .SingleOrDefaultAsync(s => s.Id == id);
        if (skill is null) return TypedResults.NotFound();

        return TypedResults.Ok(ToDto(skill));
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
        var skill = await db.Skills.SingleOrDefaultAsync(s => s.Id == id);
        if (skill is null) return TypedResults.NotFound();

        var usedInGame = await db.GameSkills.AnyAsync(gs => gs.SkillId == id);
        if (usedInGame)
        {
            return TypedResults.Conflict(new ProblemDetails
            {
                Title = "Tuto dovednost nelze smazat — je součástí alespoň jedné hry.",
                Status = StatusCodes.Status409Conflict
            });
        }

        var usedInRecipe = await db.CraftingSkillRequirements.AnyAsync(csr => csr.SkillId == id);
        if (usedInRecipe)
        {
            return TypedResults.Conflict(new ProblemDetails
            {
                Title = "Tuto dovednost nelze smazat — je vyžadována alespoň jedním receptem.",
                Status = StatusCodes.Status409Conflict
            });
        }

        db.Skills.Remove(skill);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
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

    private static SkillDto ToDto(Skill skill) => new(
        skill.Id,
        skill.Name,
        skill.ClassRestriction,
        skill.Effect,
        skill.RequirementNotes,
        skill.ImagePath,
        skill.BuildingRequirements.Select(r => r.BuildingId).ToList());
}
