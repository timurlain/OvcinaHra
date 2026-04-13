using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using OvcinaHra.Api.Data;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Services;

public class RegistraceImportService(HttpClient httpClient, IConfiguration configuration, WorldDbContext db)
{
    private readonly string _baseUrl = configuration["IntegrationApi:BaseUrl"] ?? "https://registrace.ovcina.cz";
    private readonly string? _apiKey = configuration["IntegrationApi:ApiKey"];

    public async Task<ImportResultDto> ImportAsync(int gameId)
    {
        var created = 0;
        var updated = 0;
        var skipped = 0;
        var errors = new List<string>();

        List<RegistraceCharacterRecord> records;
        try
        {
            records = await FetchCharactersAsync(gameId);
        }
        catch (Exception ex)
        {
            return new ImportResultDto(0, 0, 0, [$"Failed to fetch from registrace: {ex.Message}"]);
        }

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
                        Race = record.Race,
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
                    character.Race = record.Race;
                    character.BirthYear = record.PersonBirthYear;
                    character.UpdatedAtUtc = DateTime.UtcNow;

                    // If the character was soft-deleted (e.g., from a previous import),
                    // restore it — this person is registered for the current game.
                    if (character.IsDeleted)
                        character.IsDeleted = false;
                }

                // Look up assignment by GameId + ExternalPersonId
                var assignment = await db.CharacterAssignments
                    .FirstOrDefaultAsync(a => a.GameId == gameId && a.ExternalPersonId == record.PersonId);

                PlayerClass? playerClass = null;
                if (!string.IsNullOrWhiteSpace(record.ClassOrType))
                    Enum.TryParse<PlayerClass>(record.ClassOrType, ignoreCase: true, out var pc)
                        .WhenTrue(pc, ref playerClass);

                if (assignment is null)
                {
                    assignment = new CharacterAssignment
                    {
                        CharacterId = character.Id,
                        GameId = gameId,
                        ExternalPersonId = record.PersonId,
                        RegistraceCharacterId = record.CharacterId,
                        Class = playerClass,
                        Kingdom = record.KingdomName,
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
                    assignment.Kingdom = record.KingdomName;
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

    private async Task<List<RegistraceCharacterRecord>> FetchCharactersAsync(int gameId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"{_baseUrl.TrimEnd('/')}/api/v1/games/{gameId}/characters");

        if (!string.IsNullOrWhiteSpace(_apiKey))
            request.Headers.Add("X-Api-Key", _apiKey);

        var response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<List<RegistraceCharacterRecord>>(json,
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
