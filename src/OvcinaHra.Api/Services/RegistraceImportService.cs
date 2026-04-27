using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using OvcinaHra.Api.Data;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;
using OvcinaHra.Shared.Extensions;

namespace OvcinaHra.Api.Services;

/// <summary>
/// Thrown when a caller asks the import service to do work for a local
/// OvčinaHra Game that has no <c>ExternalGameId</c> (#191). Endpoints
/// translate this into a 400 ProblemDetails so the UI can surface a
/// "není propojená s registrací" CTA pointing at the link picker (#187).
/// </summary>
public sealed class GameNotLinkedToRegistraceException(int localGameId)
    : Exception($"Game {localGameId} is not linked to a registrace game (Game.ExternalGameId is null).")
{
    public int LocalGameId { get; } = localGameId;
}

/// <summary>
/// Thrown when the import service is asked to operate on a local game
/// that doesn't exist (#191). Distinct from
/// <see cref="GameNotLinkedToRegistraceException"/> so endpoints can
/// return 404 vs 400 — Copilot flagged the original conflation.
/// </summary>
public sealed class GameNotFoundException(int localGameId)
    : Exception($"Local game {localGameId} does not exist.")
{
    public int LocalGameId { get; } = localGameId;
}

/// <summary>
/// Issue #191 — single source of truth for the Czech ProblemDetails
/// strings the import endpoints surface. Lets us tweak copy or wire a
/// new CTA without hunting both endpoints.
/// </summary>
public static class RegistraceImportProblems
{
    public const string NotLinkedTitle = "Hra není propojená s registrací.";
    public const string NotLinkedDetail =
        "Tato hra ještě není propojená s registrací. Otevřete Správu her, otevřete tuto hru a klikněte na tlačítko Propojit s registrací.";

    public const string NotFoundTitle = "Hra nenalezena.";
    public static string NotFoundDetail(int localGameId) =>
        $"Hra s ID {localGameId} v této instanci OvčinaHra neexistuje.";

    public const string TimeoutTitle = "Registrační služba neodpovídá.";
    public const string TimeoutDetail =
        "Registrační služba neodpovídá v čase, zkuste to prosím za chvíli.";
}

public class RegistraceImportService(
    HttpClient httpClient,
    IConfiguration configuration,
    WorldDbContext db,
    ILogger<RegistraceImportService> logger)
{
    private readonly string _baseUrl = configuration["IntegrationApi:BaseUrl"] ?? "https://registrace.ovcina.cz";
    private readonly string? _apiKey = configuration["IntegrationApi:ApiKey"];

    /// <summary>
    /// Issue #191 — resolve the registrace counterpart id for a local game.
    /// Three outcomes:
    /// <list type="bullet">
    ///   <item>game exists + linked → returns <c>ExternalGameId</c></item>
    ///   <item>game exists + unlinked → throws <see cref="GameNotLinkedToRegistraceException"/></item>
    ///   <item>game does not exist → throws <see cref="GameNotFoundException"/></item>
    /// </list>
    /// The two exception types let endpoints return 400 vs 404 distinctly
    /// — pre-fixup the missing-game case was masquerading as not-linked.
    /// </summary>
    private async Task<int> ResolveExternalGameIdAsync(int localGameId)
    {
        // Fetch a wrapper struct so a null ExternalGameId on an existing
        // game doesn't collapse to the same "no row" sentinel. Without
        // this projection the FirstOrDefault would return 0/null both
        // when the game is missing and when it's unlinked.
        var row = await db.Games
            .Where(g => g.Id == localGameId)
            .Select(g => new { g.ExternalGameId })
            .FirstOrDefaultAsync();
        if (row is null)
            throw new GameNotFoundException(localGameId);
        if (row.ExternalGameId is null)
            throw new GameNotLinkedToRegistraceException(localGameId);
        return row.ExternalGameId.Value;
    }

    /// <summary>
    /// Imports player characters for the local OvčinaHra game with id
    /// <paramref name="localGameId"/>. Resolves the registrace counterpart
    /// from <c>Game.ExternalGameId</c> internally — callers no longer need
    /// to (and should not) hand over a registrace id directly.
    /// Network/parse failures are folded into <c>ImportResultDto.Errors</c>
    /// — the original behavior the standalone "Importovat" button relies on.
    /// Catches <see cref="HttpRequestException"/> and
    /// <see cref="System.Text.Json.JsonException"/> (malformed upstream payload)
    /// so the button surfaces a Czech error rather than a 500.
    /// <see cref="TaskCanceledException"/> is intentionally left for endpoints
    /// to translate into 504 Gateway Timeout.
    /// </summary>
    public async Task<ImportResultDto> ImportAsync(int localGameId)
    {
        try
        {
            return await ImportOrThrowAsync(localGameId);
        }
        catch (HttpRequestException ex)
        {
            return new ImportResultDto(0, 0, 0, [$"Failed to fetch from registrace: {ex.Message}"]);
        }
        catch (JsonException ex)
        {
            return new ImportResultDto(0, 0, 0, [$"Failed to parse registrace response: {ex.Message}"]);
        }
    }

    /// <summary>
    /// Issue #192 — same upsert as <see cref="ImportAsync"/> but
    /// propagates upstream <see cref="HttpRequestException"/> instead of
    /// folding them into the result. The reimport endpoint relies on this
    /// to roll back the wipe transaction when registrace is unreachable —
    /// without it we'd commit an empty database after silently swallowing
    /// the fetch failure.
    /// </summary>
    public async Task<ImportResultDto> ImportOrThrowAsync(int localGameId)
    {
        var externalGameId = await ResolveExternalGameIdAsync(localGameId);
        var created = 0;
        var updated = 0;
        var skipped = 0;
        var errors = new List<string>();

        var records = await FetchCharactersAsync(externalGameId);

        // Kingdom-name → id lookup, loaded once per import. Names from registrace
        // are matched case-insensitively against the Kingdom lookup table.
        var kingdomByName = await db.Kingdoms
            .ToDictionaryAsync(k => k.Name, k => k.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var record in records)
        {
            try
            {
                // Look up Character by ExternalPersonId (including deleted)
                var character = await db.Characters
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(c => c.ExternalPersonId == record.PersonId);

                if (character is null)
                {
                    var name = !string.IsNullOrWhiteSpace(record.CharacterName)
                        ? record.CharacterName
                        : $"{record.PersonFirstName} {record.PersonLastName}".Trim();

                    character = new Character
                    {
                        Name = name,
                        PlayerFirstName = record.PersonFirstName,
                        PlayerLastName = record.PersonLastName,
                        Race = RaceExtensions.TryParseRace(record.Race),
                        BirthYear = record.PersonBirthYear,
                        IsPlayedCharacter = true,
                        ExternalPersonId = record.PersonId,
                        CreatedAtUtc = DateTime.UtcNow,
                        UpdatedAtUtc = DateTime.UtcNow
                    };
                    db.Characters.Add(character);
                    await db.SaveChangesAsync();
                }
                else
                {
                    // Update persistent lore attributes from latest registrace data
                    // Name is intentionally NOT updated — it's user-editable in our app
                    character.PlayerFirstName = record.PersonFirstName;
                    character.PlayerLastName = record.PersonLastName;
                    character.Race = RaceExtensions.TryParseRace(record.Race);
                    character.BirthYear = record.PersonBirthYear;
                    character.UpdatedAtUtc = DateTime.UtcNow;

                    // If the character was soft-deleted (e.g., from a previous import),
                    // restore it — this person is registered for the current game.
                    if (character.IsDeleted)
                        character.IsDeleted = false;
                }

                // Look up assignment by local GameId + ExternalPersonId.
                // Pre-#191 this used the same `gameId` variable that was
                // also passed to the registrace fetch URL — broken if the
                // local id and registrace id ever diverge. Now resolved
                // separately at the top of ImportAsync.
                var assignment = await db.CharacterAssignments
                    .FirstOrDefaultAsync(a => a.GameId == localGameId && a.ExternalPersonId == record.PersonId);

                PlayerClass? playerClass = null;
                if (!string.IsNullOrWhiteSpace(record.ClassOrType))
                    Enum.TryParse<PlayerClass>(record.ClassOrType, ignoreCase: true, out var pc)
                        .WhenTrue(pc, ref playerClass);

                int? kingdomId = null;
                if (!string.IsNullOrWhiteSpace(record.KingdomName)
                    && kingdomByName.TryGetValue(record.KingdomName, out var kid))
                {
                    kingdomId = kid;
                }

                if (assignment is null)
                {
                    assignment = new CharacterAssignment
                    {
                        CharacterId = character.Id,
                        GameId = localGameId,
                        ExternalPersonId = record.PersonId,
                        RegistraceCharacterId = record.CharacterId,
                        Class = playerClass,
                        KingdomId = kingdomId,
                        IsActive = true,
                        StartedAtUtc = DateTime.UtcNow
                    };
                    db.CharacterAssignments.Add(assignment);
                    created++;
                }
                else
                {
                    assignment.RegistraceCharacterId = record.CharacterId;
                    assignment.Class = playerClass;
                    assignment.KingdomId = kingdomId;
                    updated++;
                }

                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                errors.Add($"PersonId {record.PersonId}: {ex.Message}");
                skipped++;
            }
        }

        return new ImportResultDto(created, updated, skipped, errors);
    }

    private async Task<List<RegistraceCharacterRecord>> FetchCharactersAsync(int externalGameId)
    {
        var endpoint = $"/api/v1/games/{externalGameId}/characters";
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"{_baseUrl.TrimEnd('/')}{endpoint}");

        if (!string.IsNullOrWhiteSpace(_apiKey))
            request.Headers.Add("X-Api-Key", _apiKey);

        using var response = await SendAsync(request, endpoint);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<List<RegistraceCharacterRecord>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
    }

    /// <summary>
    /// Fetches the registrace "adults" list for the local game with id
    /// <paramref name="localGameId"/>. Used by the NPC picker to let
    /// organizers attach a real-world person to an NPC. Resolves
    /// <c>Game.ExternalGameId</c> internally — see <see cref="ImportAsync"/>
    /// for the same #191 contract.
    /// </summary>
    public async Task<List<RegistraceAdultDto>> FetchAdultsAsync(int localGameId)
    {
        var externalGameId = await ResolveExternalGameIdAsync(localGameId);

        var endpoint = $"/api/v1/games/{externalGameId}/adults";
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"{_baseUrl.TrimEnd('/')}{endpoint}");

        if (!string.IsNullOrWhiteSpace(_apiKey))
            request.Headers.Add("X-Api-Key", _apiKey);

        using var response = await SendAsync(request, endpoint);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<List<RegistraceAdultDto>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, string endpoint)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["Endpoint"] = endpoint
        });
        var sw = Stopwatch.StartNew();
        try
        {
            var response = await httpClient.SendAsync(request);
            logger.LogInformation(
                "Registrace upstream {Endpoint} completed in {ElapsedMs} ms with {Outcome}. StatusCode: {StatusCode}",
                endpoint,
                sw.ElapsedMilliseconds,
                response.IsSuccessStatusCode ? "success" : "error",
                response.StatusCode);
            return response;
        }
        catch (TaskCanceledException ex)
        {
            logger.LogInformation(
                ex,
                "Registrace upstream {Endpoint} completed in {ElapsedMs} ms with {Outcome}",
                endpoint, sw.ElapsedMilliseconds, "timeout");
            throw;
        }
        catch (Exception ex)
        {
            logger.LogInformation(
                ex,
                "Registrace upstream {Endpoint} completed in {ElapsedMs} ms with {Outcome}",
                endpoint, sw.ElapsedMilliseconds, "error");
            throw;
        }
    }

    private sealed record RegistraceCharacterRecord(
        [property: JsonPropertyName("characterId")] int CharacterId,
        [property: JsonPropertyName("personId")] int PersonId,
        [property: JsonPropertyName("personFirstName")] string PersonFirstName,
        [property: JsonPropertyName("personLastName")] string PersonLastName,
        [property: JsonPropertyName("personBirthYear")] int? PersonBirthYear,
        [property: JsonPropertyName("characterName")] string? CharacterName,
        [property: JsonPropertyName("race")] string? Race,
        [property: JsonPropertyName("classOrType")] string? ClassOrType,
        [property: JsonPropertyName("kingdomName")] string? KingdomName,
        [property: JsonPropertyName("kingdomId")] int? KingdomId,
        [property: JsonPropertyName("levelReached")] int? LevelReached,
        [property: JsonPropertyName("continuityStatus")] string? ContinuityStatus);
}

internal static class BoolExtensions
{
    internal static void WhenTrue(this bool result, PlayerClass value, ref PlayerClass? target)
    {
        if (result) target = value;
    }
}
