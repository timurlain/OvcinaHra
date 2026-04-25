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

public class RegistraceImportService(HttpClient httpClient, IConfiguration configuration, WorldDbContext db)
{
    private readonly string _baseUrl = configuration["IntegrationApi:BaseUrl"] ?? "https://registrace.ovcina.cz";
    private readonly string? _apiKey = configuration["IntegrationApi:ApiKey"];

    /// <summary>
    /// Issue #191 — resolve the registrace counterpart id for a local game.
    /// Throws <see cref="GameNotLinkedToRegistraceException"/> when not linked
    /// rather than silently using the wrong id (the pre-#191 behavior).
    /// </summary>
    private async Task<int> ResolveExternalGameIdAsync(int localGameId)
    {
        var externalId = await db.Games
            .Where(g => g.Id == localGameId)
            .Select(g => g.ExternalGameId)
            .FirstOrDefaultAsync();
        if (externalId is null)
            throw new GameNotLinkedToRegistraceException(localGameId);
        return externalId.Value;
    }

    /// <summary>
    /// Imports player characters for the local OvčinaHra game with id
    /// <paramref name="localGameId"/>. Resolves the registrace counterpart
    /// from <c>Game.ExternalGameId</c> internally — callers no longer need
    /// to (and should not) hand over a registrace id directly.
    /// </summary>
    public async Task<ImportResultDto> ImportAsync(int localGameId)
    {
        var externalGameId = await ResolveExternalGameIdAsync(localGameId);
        var created = 0;
        var updated = 0;
        var skipped = 0;
        var errors = new List<string>();

        List<RegistraceCharacterRecord> records;
        try
        {
            records = await FetchCharactersAsync(externalGameId);
        }
        catch (Exception ex)
        {
            return new ImportResultDto(0, 0, 0, [$"Failed to fetch from registrace: {ex.Message}"]);
        }

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
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"{_baseUrl.TrimEnd('/')}/api/v1/games/{externalGameId}/characters");

        if (!string.IsNullOrWhiteSpace(_apiKey))
            request.Headers.Add("X-Api-Key", _apiKey);

        var response = await httpClient.SendAsync(request);
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

        var request = new HttpRequestMessage(HttpMethod.Get,
            $"{_baseUrl.TrimEnd('/')}/api/v1/games/{externalGameId}/adults");

        if (!string.IsNullOrWhiteSpace(_apiKey))
            request.Headers.Add("X-Api-Key", _apiKey);

        var response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<List<RegistraceAdultDto>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
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
