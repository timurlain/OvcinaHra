using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Services;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Endpoints;

public static class NpcEndpoints
{
    public static RouteGroupBuilder MapNpcEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/npcs").WithTags("Npcs");

        group.MapGet("/", GetAll);
        group.MapGet("/{id:int}", GetById);
        group.MapPost("/", Create);
        group.MapPut("/{id:int}", Update);
        group.MapDelete("/{id:int}", Delete);

        // Per-game assignment
        group.MapGet("/by-game/{gameId:int}", GetByGame);
        group.MapPost("/game-npc", CreateGameNpc);
        group.MapPut("/game-npc/{gameId:int}/{npcId:int}", UpdateGameNpc);
        group.MapDelete("/game-npc/{gameId:int}/{npcId:int}", DeleteGameNpc);

        // Eligible players (adults from registrace) for the NPC picker.
        group.MapGet("/available-players/{gameId:int}", GetAvailablePlayers);

        return group;
    }

    private static async Task<Results<Ok<List<RegistraceAdultDto>>, ProblemHttpResult>> GetAvailablePlayers(
        int gameId, RegistraceImportService registrace)
    {
        try
        {
            var adults = await registrace.FetchAdultsAsync(gameId);
            return TypedResults.Ok(adults);
        }
        catch (GameNotLinkedToRegistraceException)
        {
            // Issue #191 — same shape as the character import 400 path.
            return TypedResults.Problem(
                detail: "Tato hra ještě není propojená s registrací. Otevřete Správu her, otevřete tuto hru a klikněte na tlačítko Propojit s registrací.",
                title: "Hra není propojená s registrací.",
                statusCode: StatusCodes.Status400BadRequest);
        }
        catch (HttpRequestException ex)
        {
            return TypedResults.Problem(
                detail: $"Registrace unreachable: {ex.Message}",
                statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static async Task<Ok<List<NpcListDto>>> GetAll(WorldDbContext db)
    {
        var npcs = await db.Npcs
            .Where(n => !n.IsDeleted)
            .OrderBy(n => n.Name)
            .Select(n => new NpcListDto(n.Id, n.Name, n.Role, n.Description, n.BirthYear, n.DeathYear))
            .ToListAsync();
        return TypedResults.Ok(npcs);
    }

    private static async Task<Results<Ok<NpcDetailDto>, NotFound>> GetById(int id, WorldDbContext db)
    {
        var n = await db.Npcs.FirstOrDefaultAsync(n => n.Id == id && !n.IsDeleted);
        if (n is null) return TypedResults.NotFound();

        return TypedResults.Ok(new NpcDetailDto(
            n.Id, n.Name, n.Role, n.Description, n.Notes, n.ImagePath, n.BirthYear, n.DeathYear));
    }

    private static async Task<Created<NpcDetailDto>> Create(CreateNpcDto dto, WorldDbContext db)
    {
        var n = new Npc
        {
            Name = dto.Name,
            Role = dto.Role,
            Description = dto.Description,
            Notes = dto.Notes,
            BirthYear = dto.BirthYear,
            DeathYear = dto.DeathYear
        };
        db.Npcs.Add(n);
        await db.SaveChangesAsync();

        return TypedResults.Created($"/api/npcs/{n.Id}",
            new NpcDetailDto(n.Id, n.Name, n.Role, n.Description, n.Notes, n.ImagePath, n.BirthYear, n.DeathYear));
    }

    private static async Task<Results<NoContent, NotFound>> Update(int id, UpdateNpcDto dto, WorldDbContext db)
    {
        var n = await db.Npcs.FirstOrDefaultAsync(n => n.Id == id && !n.IsDeleted);
        if (n is null) return TypedResults.NotFound();

        n.Name = dto.Name;
        n.Role = dto.Role;
        n.Description = dto.Description;
        n.Notes = dto.Notes;
        n.BirthYear = dto.BirthYear;
        n.DeathYear = dto.DeathYear;
        n.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound>> Delete(int id, WorldDbContext db)
    {
        var n = await db.Npcs.FirstOrDefaultAsync(n => n.Id == id && !n.IsDeleted);
        if (n is null) return TypedResults.NotFound();

        n.IsDeleted = true;
        n.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Ok<List<GameNpcDto>>> GetByGame(int gameId, WorldDbContext db)
    {
        var npcs = await db.GameNpcs
            .Where(gn => gn.GameId == gameId)
            .Include(gn => gn.Npc)
            .OrderBy(gn => gn.Npc.Name)
            .Select(gn => new GameNpcDto(
                gn.GameId, gn.NpcId, gn.Npc.Name, gn.Npc.Role,
                gn.PlayedByPersonId, gn.PlayedByName, gn.PlayedByEmail,
                gn.Notes))
            .ToListAsync();
        return TypedResults.Ok(npcs);
    }

    private static async Task<Results<Created<GameNpcDto>, Conflict>> CreateGameNpc(CreateGameNpcDto dto, WorldDbContext db)
    {
        if (await db.GameNpcs.AnyAsync(gn => gn.GameId == dto.GameId && gn.NpcId == dto.NpcId))
            return TypedResults.Conflict();

        db.GameNpcs.Add(new GameNpc
        {
            GameId = dto.GameId,
            NpcId = dto.NpcId,
            PlayedByPersonId = dto.PlayedByPersonId,
            PlayedByName = dto.PlayedByName,
            PlayedByEmail = dto.PlayedByEmail,
            Notes = dto.Notes
        });
        await db.SaveChangesAsync();

        var npc = await db.Npcs.FindAsync(dto.NpcId);
        return TypedResults.Created($"/api/npcs/game-npc/{dto.GameId}/{dto.NpcId}",
            new GameNpcDto(dto.GameId, dto.NpcId, npc?.Name ?? "", npc?.Role ?? default,
                dto.PlayedByPersonId, dto.PlayedByName, dto.PlayedByEmail, dto.Notes));
    }

    private static async Task<Results<NoContent, NotFound>> UpdateGameNpc(int gameId, int npcId, UpdateGameNpcDto dto, WorldDbContext db)
    {
        var gn = await db.GameNpcs.FindAsync(gameId, npcId);
        if (gn is null) return TypedResults.NotFound();

        gn.PlayedByPersonId = dto.PlayedByPersonId;
        gn.PlayedByName = dto.PlayedByName;
        gn.PlayedByEmail = dto.PlayedByEmail;
        gn.Notes = dto.Notes;

        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound>> DeleteGameNpc(int gameId, int npcId, WorldDbContext db)
    {
        var gn = await db.GameNpcs.FindAsync(gameId, npcId);
        if (gn is null) return TypedResults.NotFound();
        db.GameNpcs.Remove(gn);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }
}
