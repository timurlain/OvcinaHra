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
        if (requiredBuildingIds.Count > 0)
        {
            var knownBuildingCount = await db.Buildings
                .CountAsync(b => requiredBuildingIds.Contains(b.Id));
            if (knownBuildingCount != requiredBuildingIds.Count)
            {
                return TypedResults.BadRequest(new ProblemDetails
                {
                    Title = "Některé z požadovaných budov neexistují.",
                    Status = StatusCodes.Status400BadRequest
                });
            }
        }

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

    private static SkillDto ToDto(Skill skill) => new(
        skill.Id,
        skill.Name,
        skill.ClassRestriction,
        skill.Effect,
        skill.RequirementNotes,
        skill.ImagePath,
        skill.BuildingRequirements.Select(r => r.BuildingId).ToList());
}
