using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Services;
using OvcinaHra.Shared.Ciphers;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;
using OvcinaHra.Shared.Extensions;

namespace OvcinaHra.Api.Endpoints;

public static class LocationCipherEndpoints
{
    private static readonly JsonSerializerOptions LogJsonOptions = new(JsonSerializerDefaults.Web);

    public static RouteGroupBuilder MapLocationCipherEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/location-ciphers")
            .WithTags("LocationCiphers")
            .RequireAuthorization()
            .AddEndpointFilter(LogEndpointException);

        group.MapGet("/by-game/{gameId:int}", GetByGame);
        group.MapGet("/by-location/{locationId:int}", GetByLocation);
        group.MapGet("/{id:int}", GetDetail);
        group.MapPost("/", Create);
        group.MapPut("/{id:int}", Update);
        group.MapDelete("/{id:int}", Delete);
        group.MapPost("/bulk-import", BulkImport);

        // Compatibility for the existing location-detail cipher panel + PDF export.
        group.MapGet("/{gameId:int}/{locationId:int}", GetByLocationLegacy);
        group.MapGet("/{gameId:int}/{locationId:int}/pdf", DownloadLocationPdf);
        group.MapGet("/{gameId:int}/{locationId:int}/{skillSlug}/pdf", DownloadSinglePdf);
        group.MapPut("/{gameId:int}/{locationId:int}/{skillSlug}", UpsertLegacy);
        group.MapDelete("/{gameId:int}/{locationId:int}/{skillSlug}", DeleteLegacy);

        var vouchers = routes.MapGroup("/api/library-vouchers")
            .WithTags("LibraryVouchers")
            .RequireAuthorization()
            .AddEndpointFilter(LogEndpointException);
        vouchers.MapGet("/", GetLibraryVouchers);
        vouchers.MapPost("/claim", ClaimVoucher);

        return group;
    }

    private static async ValueTask<object?> LogEndpointException(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (Exception ex)
        {
            var loggerFactory = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger("OvcinaHra.Api.Endpoints.LocationCipherEndpoints");
            logger.LogError(ex, "[location-cipher] exception method={Method} path={Path}",
                context.HttpContext.Request.Method,
                context.HttpContext.Request.Path.Value);
            throw;
        }
    }

    private static async Task<IResult> GetByGame(
        int gameId,
        WorldDbContext db,
        HttpContext http,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("OvcinaHra.Api.Endpoints.LocationCipherEndpoints");
        LogCipher(logger, "entry", new { endpoint = "by-game", gameId });
        if (!CanAuthor(http.User))
            return Results.Forbid();

        var rows = await db.LocationCiphers
            .AsNoTracking()
            .Include(c => c.Location)
            .Include(c => c.LinkedQuest)
            .Include(c => c.ClaimedByCharacter)
            .Where(c => c.GameId == gameId)
            .OrderBy(c => c.Location.Name)
            .ThenBy(c => c.Skill)
            .ToListAsync(ct);

        LogCipher(logger, "exit", new { endpoint = "by-game", gameId, count = rows.Count });
        return Results.Ok(rows.Select(ToDto).ToList());
    }

    private static async Task<IResult> GetByLocation(
        int locationId,
        int gameId,
        WorldDbContext db,
        HttpContext http,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("OvcinaHra.Api.Endpoints.LocationCipherEndpoints");
        LogCipher(logger, "entry", new { endpoint = "by-location", gameId, locationId });
        if (!CanAuthor(http.User))
            return Results.Forbid();

        if (!await IsParentLocationInGameAsync(db, gameId, locationId, ct))
            return Results.NotFound();

        var slots = await BuildSlotsAsync(db, gameId, locationId, ct);
        LogCipher(logger, "exit", new { endpoint = "by-location", gameId, locationId, count = slots.Count });
        return Results.Ok(slots);
    }

    private static Task<IResult> GetByLocationLegacy(
        int gameId,
        int locationId,
        WorldDbContext db,
        HttpContext http,
        ILoggerFactory loggerFactory,
        CancellationToken ct) =>
        GetByLocation(locationId, gameId, db, http, loggerFactory, ct);

    private static async Task<IResult> GetDetail(
        int id,
        WorldDbContext db,
        HttpContext http,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("OvcinaHra.Api.Endpoints.LocationCipherEndpoints");
        LogCipher(logger, "entry", new { endpoint = "detail", id });
        if (!CanAuthor(http.User))
            return Results.Forbid();

        var cipher = await LoadCipherAsync(db, id, ct);
        if (cipher is null)
            return Results.NotFound();

        LogCipher(logger, "exit", new { endpoint = "detail", id });
        return Results.Ok(ToDetailDto(cipher));
    }

    private static async Task<IResult> Create(
        LocationCipherCreateDto dto,
        WorldDbContext db,
        HttpContext http,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("OvcinaHra.Api.Endpoints.LocationCipherEndpoints");
        LogCipher(logger, "entry", new { endpoint = "create", dto.GameId, dto.LocationId, dto.Skill });
        if (!CanAuthor(http.User))
            return Results.Forbid();

        var validation = await ValidateWriteAsync(db, dto, ct);
        if (validation.Error is not null)
            return Results.BadRequest(ValidationProblem(validation.Error));

        var existing = await db.LocationCiphers
            .AnyAsync(c => c.GameId == dto.GameId && c.LocationId == dto.LocationId && c.Skill == dto.Skill, ct);
        if (existing)
            return Results.Conflict(ValidationProblem("Šifra pro tuto lokaci a dovednost už existuje."));

        var cipher = new LocationCipher
        {
            GameId = dto.GameId,
            LocationId = dto.LocationId,
            Skill = dto.Skill,
            Tier = dto.Tier,
            ContentType = dto.ContentType,
            RevealText = validation.RevealText!,
            CipherText = validation.CipherText,
            LibraryKeyword = validation.LibraryKeyword,
            LibraryReward = validation.LibraryReward,
            LinkedQuestId = dto.LinkedQuestId,
            LinkedStashNumber = dto.LinkedStashNumber,
            OrganizerNotes = NullIfWhiteSpace(dto.OrganizerNotes)
        };
        db.LocationCiphers.Add(cipher);

        LogCipher(logger, "db-write", new { endpoint = "create", dto.GameId, dto.LocationId, dto.Skill });
        await db.SaveChangesAsync(ct);

        var saved = await LoadCipherAsync(db, cipher.Id, ct);
        LogCipher(logger, "exit", new { endpoint = "create", id = cipher.Id });
        return Results.Created($"/api/location-ciphers/{cipher.Id}", ToDetailDto(saved!));
    }

    private static async Task<IResult> Update(
        int id,
        LocationCipherUpdateDto dto,
        WorldDbContext db,
        HttpContext http,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("OvcinaHra.Api.Endpoints.LocationCipherEndpoints");
        LogCipher(logger, "entry", new { endpoint = "update", id, dto.GameId, dto.LocationId, dto.Skill });
        if (!CanAuthor(http.User))
            return Results.Forbid();

        var cipher = await db.LocationCiphers.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (cipher is null)
            return Results.NotFound();

        var createDto = new LocationCipherCreateDto(
            dto.GameId,
            dto.LocationId,
            dto.Skill,
            dto.Tier,
            dto.ContentType,
            dto.RevealText,
            dto.CipherText,
            dto.LibraryKeyword,
            dto.LibraryReward,
            dto.LinkedQuestId,
            dto.LinkedStashNumber,
            dto.OrganizerNotes);
        var validation = await ValidateWriteAsync(db, createDto, ct, id);
        if (validation.Error is not null)
            return Results.BadRequest(ValidationProblem(validation.Error));

        var duplicate = await db.LocationCiphers.AnyAsync(c =>
            c.Id != id && c.GameId == dto.GameId && c.LocationId == dto.LocationId && c.Skill == dto.Skill, ct);
        if (duplicate)
            return Results.Conflict(ValidationProblem("Šifra pro tuto lokaci a dovednost už existuje."));

        ApplyWrite(cipher, createDto, validation);
        LogCipher(logger, "db-write", new { endpoint = "update", id });
        await db.SaveChangesAsync(ct);

        var saved = await LoadCipherAsync(db, id, ct);
        LogCipher(logger, "exit", new { endpoint = "update", id });
        return Results.Ok(ToDetailDto(saved!));
    }

    private static async Task<IResult> Delete(
        int id,
        WorldDbContext db,
        HttpContext http,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("OvcinaHra.Api.Endpoints.LocationCipherEndpoints");
        LogCipher(logger, "entry", new { endpoint = "delete", id });
        if (!CanDeleteOrBulk(http.User))
            return Results.Forbid();

        var cipher = await db.LocationCiphers.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (cipher is null)
            return Results.NotFound();

        db.LocationCiphers.Remove(cipher);
        LogCipher(logger, "db-write", new { endpoint = "delete", id });
        await db.SaveChangesAsync(ct);
        LogCipher(logger, "exit", new { endpoint = "delete", id });
        return Results.NoContent();
    }

    private static async Task<IResult> BulkImport(
        LocationCipherBulkImportDto dto,
        WorldDbContext db,
        HttpContext http,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("OvcinaHra.Api.Endpoints.LocationCipherEndpoints");
        LogCipher(logger, "entry", new { endpoint = "bulk-import", dto.GameId, count = dto.Ciphers.Count });
        if (!CanDeleteOrBulk(http.User))
            return Results.Forbid();

        var changedIds = new List<int>();
        var importKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        foreach (var row in dto.Ciphers)
        {
            if (row.GameId != dto.GameId)
                return Results.BadRequest(ValidationProblem("Všechny importované šifry musí mít stejné GameId jako obálka importu."));

            var cipher = await db.LocationCiphers.FirstOrDefaultAsync(c =>
                c.GameId == row.GameId && c.LocationId == row.LocationId && c.Skill == row.Skill, ct);
            var validation = await ValidateWriteAsync(db, row, ct, cipher?.Id);
            if (validation.Error is not null)
                return Results.BadRequest(ValidationProblem(validation.Error));
            if (validation.LibraryKeyword is not null && !importKeywords.Add(validation.LibraryKeyword))
                return Results.BadRequest(ValidationProblem("Knihovní heslo je v importu použité vícekrát."));

            if (cipher is null)
            {
                cipher = new LocationCipher
                {
                    GameId = row.GameId,
                    LocationId = row.LocationId,
                    Skill = row.Skill,
                    Tier = row.Tier,
                    ContentType = row.ContentType,
                    RevealText = validation.RevealText!,
                    CipherText = validation.CipherText,
                    LibraryKeyword = validation.LibraryKeyword,
                    LibraryReward = validation.LibraryReward,
                    LinkedQuestId = row.LinkedQuestId,
                    LinkedStashNumber = row.LinkedStashNumber,
                    OrganizerNotes = NullIfWhiteSpace(row.OrganizerNotes)
                };
                db.LocationCiphers.Add(cipher);
            }
            else
            {
                ApplyWrite(cipher, row, validation);
            }

            LogCipher(logger, "cipher.upsert", new { source = "bulk-import", row.GameId, row.LocationId, row.Skill });
            await db.SaveChangesAsync(ct);
            changedIds.Add(cipher.Id);
        }

        await tx.CommitAsync(ct);

        var rows = await db.LocationCiphers
            .AsNoTracking()
            .Include(c => c.Location)
            .Include(c => c.LinkedQuest)
            .Include(c => c.ClaimedByCharacter)
            .Where(c => changedIds.Contains(c.Id))
            .OrderBy(c => c.Location.Name)
            .ThenBy(c => c.Skill)
            .ToListAsync(ct);

        LogCipher(logger, "exit", new { endpoint = "bulk-import", dto.GameId, count = rows.Count });
        return Results.Ok(rows.Select(ToDto).ToList());
    }

    private static async Task<IResult> GetLibraryVouchers(
        int gameId,
        bool? onlyUnclaimed,
        WorldDbContext db,
        HttpContext http,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("OvcinaHra.Api.Endpoints.LocationCipherEndpoints");
        var unclaimedOnly = onlyUnclaimed == true;
        LogCipher(logger, "entry", new { endpoint = "library-vouchers", gameId, onlyUnclaimed = unclaimedOnly });
        if (!CanVoucherRead(http.User))
            return Results.Forbid();

        var query = db.LocationCiphers
            .AsNoTracking()
            .Include(c => c.Location)
            .Include(c => c.ClaimedByCharacter)
            .Where(c => c.GameId == gameId
                && c.LibraryKeyword != null
                && (c.Tier == CipherTier.StandardVoucher || c.Tier == CipherTier.FlagshipPaired));
        if (unclaimedOnly)
            query = query.Where(c => !c.IsClaimed);

        var rows = await query
            .OrderBy(c => c.LibraryKeyword)
            .ThenBy(c => c.Location.Name)
            .ToListAsync(ct);

        LogCipher(logger, "exit", new { endpoint = "library-vouchers", gameId, count = rows.Count });
        return Results.Ok(rows.Select(ToVoucherDto).ToList());
    }

    private static async Task<IResult> ClaimVoucher(
        int gameId,
        CipherClaimRequestDto dto,
        WorldDbContext db,
        HttpContext http,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("OvcinaHra.Api.Endpoints.LocationCipherEndpoints");
        var keyword = NormalizeKeyword(dto.LibraryKeyword);
        logger.LogInformation("[location-cipher] voucher.claim attempt keyword={Keyword} gameId={GameId}", keyword, gameId);
        if (!CanVoucherClaim(http.User))
            return Results.Forbid();

        if (keyword is null)
        {
            logger.LogInformation("[location-cipher] voucher.claim rejected reason=not-found keyword={Keyword} gameId={GameId}", dto.LibraryKeyword, gameId);
            return Results.Ok(new CipherClaimResultDto(false, "Heslo neznámé", null, null));
        }

        if (!dto.LocationStampVerified)
        {
            logger.LogInformation("[location-cipher] voucher.claim rejected reason=stamp-not-verified keyword={Keyword} gameId={GameId}", keyword, gameId);
            return Results.Ok(new CipherClaimResultDto(false, "Razítko lokace nebylo ověřeno", null, null));
        }

        var characterExists = await db.CharacterAssignments
            .AnyAsync(a => a.GameId == gameId && a.CharacterId == dto.CharacterId, ct);
        if (!characterExists)
            return Results.Ok(new CipherClaimResultDto(false, "Hrdina není v této hře", null, null));

        var cipher = await db.LocationCiphers
            .AsNoTracking()
            .Where(c => c.GameId == gameId
                && c.LibraryKeyword == keyword
                && (c.Tier == CipherTier.StandardVoucher || c.Tier == CipherTier.FlagshipPaired))
            .Select(c => new { c.Id, c.IsClaimed, c.LibraryReward })
            .FirstOrDefaultAsync(ct);

        if (cipher is null)
        {
            logger.LogInformation("[location-cipher] voucher.claim rejected reason=not-found keyword={Keyword} gameId={GameId}", keyword, gameId);
            return Results.Ok(new CipherClaimResultDto(false, "Heslo neznámé", null, null));
        }

        if (cipher.IsClaimed)
        {
            logger.LogInformation("[location-cipher] voucher.claim rejected reason=already-claimed keyword={Keyword} gameId={GameId} cipherId={CipherId}", keyword, gameId, cipher.Id);
            return Results.Ok(new CipherClaimResultDto(false, "Heslo již bylo uplatněno", null, cipher.Id));
        }

        var now = DateTime.UtcNow;
        var affected = await db.LocationCiphers
            .Where(c => c.Id == cipher.Id && !c.IsClaimed)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(c => c.IsClaimed, true)
                .SetProperty(c => c.ClaimedAtUtc, now)
                .SetProperty(c => c.ClaimedByCharacterId, dto.CharacterId), ct);

        if (affected == 0)
        {
            logger.LogInformation("[location-cipher] voucher.claim rejected reason=already-claimed keyword={Keyword} gameId={GameId} cipherId={CipherId}", keyword, gameId, cipher.Id);
            return Results.Ok(new CipherClaimResultDto(false, "Heslo již bylo uplatněno", null, cipher.Id));
        }

        logger.LogInformation("[location-cipher] voucher.claim ok cipherId={CipherId} rewardLength={RewardLength}", cipher.Id, cipher.LibraryReward?.Length ?? 0);
        return Results.Ok(new CipherClaimResultDto(true, "Uplatněno", cipher.LibraryReward, cipher.Id));
    }

    private static async Task<IResult> DownloadSinglePdf(
        int gameId,
        int locationId,
        string skillSlug,
        WorldDbContext db,
        ICipherPdfRenderer renderer,
        HttpContext http,
        CancellationToken ct)
    {
        if (!CanAuthor(http.User))
            return Results.Forbid();
        if (!AdventuringSkillExtensions.TryParseSlug(skillSlug, out var skill))
            return Results.BadRequest(ValidationProblem($"Neznámá šifrovací dovednost '{skillSlug}'."));

        var cipher = await db.LocationCiphers
            .AsNoTracking()
            .Where(c => c.GameId == gameId && c.LocationId == locationId && c.Skill == skill)
            .Select(c => new
            {
                c.CipherText,
                c.RevealText,
                LocationName = c.Location.Name
            })
            .FirstOrDefaultAsync(ct);
        if (cipher is null)
            return Results.NotFound();

        var cipherText = EnsureCipherText(cipher.CipherText, cipher.RevealText, skill);
        var pdf = renderer.RenderSingle(new CipherPdfCard(cipher.LocationName, skill, cipherText));
        return Results.File(
            pdf,
            contentType: "application/pdf",
            fileDownloadName: $"ovcina-sifra-{locationId}-{skill.GetSlug()}.pdf");
    }

    private static async Task<IResult> DownloadLocationPdf(
        int gameId,
        int locationId,
        WorldDbContext db,
        ICipherPdfRenderer renderer,
        HttpContext http,
        CancellationToken ct)
    {
        if (!CanAuthor(http.User))
            return Results.Forbid();

        var locationName = await db.GameLocations
            .Where(gl => gl.GameId == gameId && gl.LocationId == locationId)
            .Select(gl => gl.Location.Name)
            .FirstOrDefaultAsync(ct);
        if (locationName is null)
            return Results.NotFound();

        var ciphers = await db.LocationCiphers
            .AsNoTracking()
            .Where(c => c.GameId == gameId && c.LocationId == locationId)
            .Select(c => new
            {
                c.Skill,
                c.CipherText,
                c.RevealText
            })
            .ToListAsync(ct);
        if (ciphers.Count == 0)
            return Results.BadRequest(ValidationProblem("V této lokaci zatím není žádná šifra k exportu."));

        var cards = ciphers
            .OrderBy(c => c.Skill)
            .Select(c => new CipherPdfCard(locationName, c.Skill, EnsureCipherText(c.CipherText, c.RevealText, c.Skill)))
            .ToList();
        var pdf = renderer.RenderLocation(cards);

        return Results.File(
            pdf,
            contentType: "application/pdf",
            fileDownloadName: $"ovcina-sifry-{locationId}.pdf");
    }

    private static async Task<IResult> UpsertLegacy(
        int gameId,
        int locationId,
        string skillSlug,
        UpsertLocationCipherDto dto,
        WorldDbContext db,
        HttpContext http,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        if (!AdventuringSkillExtensions.TryParseSlug(skillSlug, out var skill))
            return Results.BadRequest(ValidationProblem($"Neznámá šifrovací dovednost '{skillSlug}'."));

        var rows = await db.LocationCiphers
            .Where(c => c.GameId == gameId && c.LocationId == locationId && c.Skill == skill)
            .Select(c => c.Id)
            .ToListAsync(ct);
        if (rows.Count == 0)
        {
            var result = await Create(
                new LocationCipherCreateDto(
                    gameId,
                    locationId,
                    skill,
                    dto.QuestId.HasValue ? CipherTier.QuestTied : CipherTier.Micro,
                    CipherContentType.Info,
                    dto.MessageRaw,
                    LinkedQuestId: dto.QuestId),
                db,
                http,
                loggerFactory,
                ct);
            return IsSuccessResult(result) ? Results.NoContent() : result;
        }

        var update = new LocationCipherUpdateDto(
            gameId,
            locationId,
            skill,
            dto.QuestId.HasValue ? CipherTier.QuestTied : CipherTier.Micro,
            CipherContentType.Info,
            dto.MessageRaw,
            LinkedQuestId: dto.QuestId);
        var updateResult = await Update(rows[0], update, db, http, loggerFactory, ct);
        return IsSuccessResult(updateResult) ? Results.NoContent() : updateResult;
    }

    private static async Task<IResult> DeleteLegacy(
        int gameId,
        int locationId,
        string skillSlug,
        WorldDbContext db,
        HttpContext http,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        if (!AdventuringSkillExtensions.TryParseSlug(skillSlug, out var skill))
            return Results.BadRequest(ValidationProblem($"Neznámá šifrovací dovednost '{skillSlug}'."));

        var id = await db.LocationCiphers
            .Where(c => c.GameId == gameId && c.LocationId == locationId && c.Skill == skill)
            .Select(c => (int?)c.Id)
            .FirstOrDefaultAsync(ct);
        return id is null
            ? Results.NotFound()
            : await Delete(id.Value, db, http, loggerFactory, ct);
    }

    private static async Task<List<LocationCipherSlotDto>> BuildSlotsAsync(
        WorldDbContext db,
        int gameId,
        int locationId,
        CancellationToken ct)
    {
        var rows = await db.LocationCiphers
            .AsNoTracking()
            .Include(c => c.Location)
            .Include(c => c.LinkedQuest)
            .Include(c => c.ClaimedByCharacter)
            .Where(c => c.GameId == gameId && c.LocationId == locationId)
            .ToListAsync(ct);
        var bySkill = rows.ToDictionary(c => c.Skill, ToDto);

        return AdventuringSkillExtensions.All
            .Select(skill => new LocationCipherSlotDto(
                skill,
                skill.GetSlug(),
                skill.GetDisplayName(),
                skill.GetMaxCipherLetters(),
                bySkill.GetValueOrDefault(skill)))
            .ToList();
    }

    private static async Task<LocationCipher?> LoadCipherAsync(WorldDbContext db, int id, CancellationToken ct) =>
        await db.LocationCiphers
            .Include(c => c.Location)
            .Include(c => c.LinkedQuest)
            .Include(c => c.ClaimedByCharacter)
            .FirstOrDefaultAsync(c => c.Id == id, ct);

    private static async Task<bool> IsParentLocationInGameAsync(
        WorldDbContext db,
        int gameId,
        int locationId,
        CancellationToken ct) =>
        await db.Locations.AnyAsync(l =>
            l.Id == locationId
            && l.ParentLocationId == null
            && l.GameLocations.Any(gl => gl.GameId == gameId), ct);

    private static async Task<WriteValidation> ValidateWriteAsync(
        WorldDbContext db,
        LocationCipherCreateDto dto,
        CancellationToken ct,
        int? existingCipherId = null)
    {
        if (!Enum.IsDefined(dto.Skill))
            return WriteValidation.Fail("Neznámá dobrodružná dovednost.");
        if (!Enum.IsDefined(dto.Tier))
            return WriteValidation.Fail("Neznámý tier šifry.");
        if (!Enum.IsDefined(dto.ContentType))
            return WriteValidation.Fail("Neznámý typ obsahu šifry.");

        if (!await IsParentLocationInGameAsync(db, dto.GameId, dto.LocationId, ct))
            return WriteValidation.Fail("Šifra musí patřit k rodičovské lokaci přiřazené do hry.");

        var revealText = NullIfWhiteSpace(dto.RevealText);
        if (revealText is null)
            return WriteValidation.Fail("RevealText je povinný.");
        if (revealText.Length > 500)
            return WriteValidation.Fail("RevealText má příliš mnoho znaků. Maximum je 500.");

        var cipherText = EnsureCipherText(dto.CipherText, revealText, dto.Skill);
        var cipherProblem = ValidateCipherText(cipherText, dto.Skill);
        if (cipherProblem is not null)
            return WriteValidation.Fail(cipherProblem);

        var keyword = NormalizeKeyword(dto.LibraryKeyword);
        var reward = NullIfWhiteSpace(dto.LibraryReward);
        if (dto.Tier is CipherTier.StandardVoucher or CipherTier.FlagshipPaired && (keyword is null || reward is null))
            return WriteValidation.Fail("Knihovní voucher musí mít heslo i odměnu.");
        if (dto.ContentType == CipherContentType.Keyword && keyword is null)
            return WriteValidation.Fail("Typ Klíčové slovo musí mít vyplněné heslo.");
        if (keyword is { Length: > 50 })
            return WriteValidation.Fail("LibraryKeyword má příliš mnoho znaků. Maximum je 50.");
        if (keyword is not null)
        {
            var keywordExists = await db.LocationCiphers.AnyAsync(c =>
                c.GameId == dto.GameId
                && c.LibraryKeyword == keyword
                && (!existingCipherId.HasValue || c.Id != existingCipherId.Value), ct);
            if (keywordExists)
                return WriteValidation.Fail("Knihovní heslo už v této hře existuje.");
        }

        if (reward is { Length: > 500 })
            return WriteValidation.Fail("LibraryReward má příliš mnoho znaků. Maximum je 500.");
        if (dto.ContentType == CipherContentType.Pytlik && (!dto.LinkedStashNumber.HasValue || dto.LinkedStashNumber.Value <= 0))
            return WriteValidation.Fail("Typ Pytlík musí mít číslo pytlíku.");
        if (dto.OrganizerNotes is { Length: > 1000 })
            return WriteValidation.Fail("OrganizerNotes má příliš mnoho znaků. Maximum je 1000.");

        if (dto.LinkedQuestId.HasValue)
        {
            var questValid = await db.Quests.AnyAsync(q =>
                q.Id == dto.LinkedQuestId.Value
                && (q.GameId == null || q.GameId == dto.GameId)
                && q.QuestLocations.Any(ql => ql.LocationId == dto.LocationId), ct);
            if (!questValid)
                return WriteValidation.Fail("Vybraný quest nepatří k této lokaci v aktuální hře.");
        }

        return new WriteValidation(null, revealText, cipherText, keyword, reward);
    }

    private static void ApplyWrite(LocationCipher cipher, LocationCipherCreateDto dto, WriteValidation validation)
    {
        cipher.GameId = dto.GameId;
        cipher.LocationId = dto.LocationId;
        cipher.Skill = dto.Skill;
        cipher.Tier = dto.Tier;
        cipher.ContentType = dto.ContentType;
        cipher.RevealText = validation.RevealText!;
        cipher.CipherText = validation.CipherText;
        cipher.LibraryKeyword = validation.LibraryKeyword;
        cipher.LibraryReward = validation.LibraryReward;
        cipher.LinkedQuestId = dto.LinkedQuestId;
        cipher.LinkedStashNumber = dto.LinkedStashNumber;
        cipher.OrganizerNotes = NullIfWhiteSpace(dto.OrganizerNotes);
    }

    private static string EnsureCipherText(string? cipherText, string revealText, AdventuringSkill skill)
    {
        var source = string.IsNullOrWhiteSpace(cipherText) ? revealText : cipherText;
        var normalized = CipherTextNormalizer.NormalizeMessage(source);
        if (normalized.Length == 0)
            return "";
        return normalized.StartsWith("XOX", StringComparison.Ordinal)
            && normalized.EndsWith("XOX", StringComparison.Ordinal)
            && normalized.Length >= 6
            ? normalized
            : $"XOX{normalized}XOX";
    }

    private static string? ValidateCipherText(string cipherText, AdventuringSkill skill)
    {
        if (cipherText.Length == 0)
            return "CipherText musí obsahovat alespoň jedno písmeno A-Z.";
        if (!cipherText.StartsWith("XOX", StringComparison.Ordinal) || !cipherText.EndsWith("XOX", StringComparison.Ordinal))
            return "CipherText musí mít XOX wrapper.";

        var inner = cipherText[3..^3];
        var max = skill.GetMaxCipherLetters();
        if (inner.Length > max)
            return $"CipherText pro {skill.GetDisplayName()} má {inner.Length} znaků bez wrapperu. Maximum je {max}.";

        return null;
    }

    private static string? NormalizeKeyword(string? value)
    {
        var normalized = CipherTextNormalizer.NormalizeMessage(value ?? "");
        return normalized.Length == 0 ? null : normalized;
    }

    private static LocationCipherDto ToDto(LocationCipher c) => new(
        c.Id,
        c.GameId,
        c.LocationId,
        c.Location.Name,
        c.Skill,
        c.Skill.GetSlug(),
        c.Skill.GetDisplayName(),
        c.Skill.GetMaxCipherLetters(),
        c.Tier,
        c.ContentType,
        c.IsClaimable,
        c.IsClaimed,
        c.RevealText,
        c.CipherText,
        c.LibraryKeyword,
        c.LibraryReward,
        c.LinkedQuestId,
        c.LinkedQuest?.Name,
        c.LinkedStashNumber,
        c.OrganizerNotes,
        c.ClaimedAtUtc,
        c.ClaimedByCharacterId,
        c.ClaimedByCharacter?.Name);

    private static LocationCipherDetailDto ToDetailDto(LocationCipher c) => new(
        c.Id,
        c.GameId,
        c.LocationId,
        c.Location.Name,
        c.Skill,
        c.Skill.GetSlug(),
        c.Skill.GetDisplayName(),
        c.Skill.GetMaxCipherLetters(),
        c.Tier,
        c.ContentType,
        c.IsClaimable,
        c.IsClaimed,
        c.RevealText,
        c.CipherText,
        c.LibraryKeyword,
        c.LibraryReward,
        c.LinkedQuestId,
        c.LinkedQuest?.Name,
        c.LinkedStashNumber,
        c.OrganizerNotes,
        c.ClaimedAtUtc,
        c.ClaimedByCharacterId,
        c.ClaimedByCharacter?.Name);

    private static LibraryVoucherDto ToVoucherDto(LocationCipher c) => new(
        c.Id,
        c.LibraryKeyword!,
        c.LibraryReward,
        c.LocationId,
        c.Location.Name,
        c.IsClaimed,
        c.ClaimedAtUtc,
        c.Skill,
        c.Skill.GetDisplayName(),
        c.ClaimedByCharacterId,
        c.ClaimedByCharacter?.Name);

    private static bool CanAuthor(ClaimsPrincipal user) =>
        HasAnyRole(user, "Admin", "Administrator", "Osud", "Organizer", "Organizator", "Organizátor", "Service");

    private static bool CanDeleteOrBulk(ClaimsPrincipal user) =>
        HasAnyRole(user, "Admin", "Administrator", "Osud", "Organizer", "Organizator", "Organizátor", "Service");

    private static bool CanVoucherRead(ClaimsPrincipal user) =>
        HasAnyRole(user, "Admin", "Administrator", "Osud", "Knihovník", "Knihovnik", "Organizer", "Organizator", "Organizátor", "Service");

    private static bool CanVoucherClaim(ClaimsPrincipal user) => CanVoucherRead(user);

    private static bool HasAnyRole(ClaimsPrincipal user, params string[] allowed)
    {
        var roles = user.FindAll("role").Select(c => c.Value)
            .Concat(user.FindAll(ClaimTypes.Role).Select(c => c.Value));
        return roles.Any(role => allowed.Any(a => string.Equals(a, role, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool IsSuccessResult(IResult result) =>
        result.GetType().Name.Contains("Ok", StringComparison.Ordinal)
        || result.GetType().Name.Contains("Created", StringComparison.Ordinal)
        || result.GetType().Name.Contains("NoContent", StringComparison.Ordinal);

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void LogCipher(ILogger logger, string eventName, object payload)
    {
        var json = JsonSerializer.Serialize(new { Event = eventName, Payload = payload }, LogJsonOptions);
        logger.LogInformation("[location-cipher] {Payload}", json);
    }

    private static ProblemDetails ValidationProblem(string detail) => new()
    {
        Title = "Šifru nejde uložit",
        Detail = detail,
        Status = StatusCodes.Status400BadRequest
    };

    private sealed record WriteValidation(
        string? Error,
        string? RevealText,
        string? CipherText,
        string? LibraryKeyword,
        string? LibraryReward)
    {
        public static WriteValidation Fail(string error) => new(error, null, null, null, null);
    }
}
