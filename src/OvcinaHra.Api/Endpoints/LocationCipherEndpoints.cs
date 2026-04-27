using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OvcinaHra.Api.Data;
using OvcinaHra.Shared.Ciphers;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;
using OvcinaHra.Shared.Extensions;

namespace OvcinaHra.Api.Endpoints;

public static class LocationCipherEndpoints
{
    public static RouteGroupBuilder MapLocationCipherEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/location-ciphers").WithTags("LocationCiphers");

        group.MapGet("/{gameId:int}/{locationId:int}", GetByLocation);
        group.MapPut("/{gameId:int}/{locationId:int}/{skillSlug}", Upsert);
        group.MapDelete("/{gameId:int}/{locationId:int}/{skillSlug}", Delete);

        return group;
    }

    private static async Task<Results<Ok<List<LocationCipherSlotDto>>, NotFound>> GetByLocation(
        int gameId, int locationId, WorldDbContext db)
    {
        if (!await IsLocationInGameAsync(db, gameId, locationId))
            return TypedResults.NotFound();

        var rows = await db.LocationCiphers
            .Where(c => c.GameId == gameId && c.LocationId == locationId)
            .Select(c => new
            {
                c.Id,
                c.GameId,
                c.LocationId,
                c.SkillKey,
                c.MessageRaw,
                c.MessageNormalized,
                c.QuestId,
                QuestName = c.Quest == null ? null : c.Quest.Name
            })
            .ToListAsync();

        var bySkill = rows.ToDictionary(c => c.SkillKey, c => ToDto(
            c.Id, c.GameId, c.LocationId, c.SkillKey, c.MessageRaw,
            c.MessageNormalized, c.QuestId, c.QuestName));

        var slots = CipherSkillKeyExtensions.All
            .Select(skill => new LocationCipherSlotDto(
                skill,
                skill.GetSlug(),
                skill.GetDisplayName(),
                skill.GetMaxMessageLetters(),
                bySkill.GetValueOrDefault(skill)))
            .ToList();

        return TypedResults.Ok(slots);
    }

    private static async Task<Results<NoContent, NotFound, BadRequest<ProblemDetails>>> Upsert(
        int gameId, int locationId, string skillSlug, UpsertLocationCipherDto dto, WorldDbContext db)
    {
        if (!CipherSkillKeyExtensions.TryParseSlug(skillSlug, out var skillKey))
            return TypedResults.BadRequest(ValidationProblem($"Neznámá šifrovací dovednost '{skillSlug}'."));

        if (!await IsLocationInGameAsync(db, gameId, locationId))
            return TypedResults.NotFound();

        var rawMessage = dto.MessageRaw?.Trim() ?? "";
        var normalized = CipherTextNormalizer.NormalizeMessage(rawMessage);
        var validationProblem = ValidateMessage(normalized, skillKey);
        if (validationProblem is not null)
            return TypedResults.BadRequest(validationProblem);

        if (dto.QuestId.HasValue && !await IsQuestValidForLocationAsync(db, gameId, locationId, dto.QuestId.Value))
            return TypedResults.BadRequest(ValidationProblem("Vybraný quest nepatří k této lokaci v aktuální hře."));

        var cipher = await db.LocationCiphers.FirstOrDefaultAsync(c =>
            c.GameId == gameId && c.LocationId == locationId && c.SkillKey == skillKey);

        if (cipher is null)
        {
            cipher = new LocationCipher
            {
                GameId = gameId,
                LocationId = locationId,
                SkillKey = skillKey,
                MessageRaw = rawMessage,
                MessageNormalized = normalized,
                QuestId = dto.QuestId
            };
            db.LocationCiphers.Add(cipher);
        }
        else
        {
            cipher.MessageRaw = rawMessage;
            cipher.MessageNormalized = normalized;
            cipher.QuestId = dto.QuestId;
        }

        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound, BadRequest<ProblemDetails>>> Delete(
        int gameId, int locationId, string skillSlug, WorldDbContext db)
    {
        if (!CipherSkillKeyExtensions.TryParseSlug(skillSlug, out var skillKey))
            return TypedResults.BadRequest(ValidationProblem($"Neznámá šifrovací dovednost '{skillSlug}'."));

        var cipher = await db.LocationCiphers.FirstOrDefaultAsync(c =>
            c.GameId == gameId && c.LocationId == locationId && c.SkillKey == skillKey);
        if (cipher is null)
            return TypedResults.NotFound();

        db.LocationCiphers.Remove(cipher);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<bool> IsLocationInGameAsync(WorldDbContext db, int gameId, int locationId) =>
        await db.GameLocations.AnyAsync(gl => gl.GameId == gameId && gl.LocationId == locationId);

    private static async Task<bool> IsQuestValidForLocationAsync(
        WorldDbContext db, int gameId, int locationId, int questId) =>
        await db.Quests.AnyAsync(q =>
            q.Id == questId
            && (q.GameId == null || q.GameId == gameId)
            && q.QuestLocations.Any(ql => ql.LocationId == locationId));

    private static ProblemDetails? ValidateMessage(string normalized, CipherSkillKey skillKey)
    {
        if (normalized.Length == 0)
            return ValidationProblem("Zpráva musí obsahovat alespoň jedno písmeno A-Z.");

        var max = skillKey.GetMaxMessageLetters();
        if (normalized.Length > max)
            return ValidationProblem($"Zpráva pro {skillKey.GetDisplayName()} má {normalized.Length} znaků po normalizaci. Maximum je {max}.");

        return null;
    }

    private static LocationCipherDto ToDto(
        int id,
        int gameId,
        int locationId,
        CipherSkillKey skillKey,
        string messageRaw,
        string messageNormalized,
        int? questId,
        string? questName)
    {
        var encoded = $"XOX{messageNormalized}XOX";
        return new LocationCipherDto(
            id,
            gameId,
            locationId,
            skillKey,
            skillKey.GetSlug(),
            skillKey.GetDisplayName(),
            skillKey.GetMaxMessageLetters(),
            messageRaw,
            messageNormalized,
            encoded,
            encoded.Length,
            questId,
            questName);
    }

    private static ProblemDetails ValidationProblem(string detail) => new()
    {
        Title = "Šifru nejde uložit",
        Detail = detail,
        Status = StatusCodes.Status400BadRequest
    };
}
