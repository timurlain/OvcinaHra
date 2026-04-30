using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Tests.Fixtures;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Tests.Endpoints;

public class LocationCipherEndpointTests(PostgresFixture postgres) : IntegrationTestBase(postgres), IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task GetByLocation_ReturnsFiveCipherSlots()
    {
        var (game, location) = await CreateAssignedLocationAsync();

        var slots = await Client.GetFromJsonAsync<List<LocationCipherSlotDto>>(
            $"/api/location-ciphers/{game.Id}/{location.Id}");

        Assert.NotNull(slots);
        Assert.Equal(5, slots!.Count);
        Assert.Contains(slots, s => s.SkillSlug == "hledani-magie" && s.MaxMessageLetters == 74);
        Assert.Contains(slots, s => s.SkillSlug == "lezeni" && s.MaxMessageLetters == 72);
        Assert.All(slots, s => Assert.Null(s.Cipher));
    }

    [Fact]
    public async Task GetByGame_ReturnsOnlyCiphersForRequestedGame()
    {
        var (game, location) = await CreateAssignedLocationAsync();
        var (otherGame, otherLocation) = await CreateAssignedLocationAsync();
        var firstResponse = await Client.PutAsJsonAsync(
            $"/api/location-ciphers/{game.Id}/{location.Id}/hledani-magie",
            new UpsertLocationCipherDto("Tady žije vlčí smečka"));
        firstResponse.EnsureSuccessStatusCode();
        var secondResponse = await Client.PutAsJsonAsync(
            $"/api/location-ciphers/{game.Id}/{location.Id}/lezeni",
            new UpsertLocationCipherDto("Vylez po skale"));
        secondResponse.EnsureSuccessStatusCode();
        var otherResponse = await Client.PutAsJsonAsync(
            $"/api/location-ciphers/{otherGame.Id}/{otherLocation.Id}/prohledavani",
            new UpsertLocationCipherDto("Cizi hra"));
        otherResponse.EnsureSuccessStatusCode();

        var ciphers = await Client.GetFromJsonAsync<List<LocationCipherDto>>(
            $"/api/location-ciphers/by-game/{game.Id}");

        Assert.NotNull(ciphers);
        Assert.Equal(2, ciphers!.Count);
        Assert.All(ciphers, c =>
        {
            Assert.Equal(game.Id, c.GameId);
            Assert.Equal(location.Id, c.LocationId);
        });
        Assert.Contains(ciphers, c => c.SkillSlug == "hledani-magie");
        Assert.Contains(ciphers, c => c.SkillSlug == "lezeni");
    }

    [Fact]
    public async Task Put_NormalizesMessage_AndReturnsPreviewOnGet()
    {
        var (game, location) = await CreateAssignedLocationAsync();
        var questId = await CreateLocationQuestAsync(game.Id, location.Id);

        var response = await Client.PutAsJsonAsync(
            $"/api/location-ciphers/{game.Id}/{location.Id}/hledani-magie",
            new UpsertLocationCipherDto("Tady žije vlčí smečka!", questId));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var slots = await Client.GetFromJsonAsync<List<LocationCipherSlotDto>>(
            $"/api/location-ciphers/{game.Id}/{location.Id}");
        var cipher = slots!.Single(s => s.SkillSlug == "hledani-magie").Cipher;

        Assert.NotNull(cipher);
        Assert.Equal("XOXTADYZIJEVLCISMECKAXOX", cipher!.MessageNormalized);
        Assert.Equal("XOXTADYZIJEVLCISMECKAXOX", cipher.EncodedPreview);
        Assert.Equal(questId, cipher.QuestId);
        Assert.Equal("Vlčí stopa", cipher.QuestName);
    }

    [Fact]
    public async Task DownloadSinglePdf_ReturnsPrintablePdf()
    {
        var (game, location) = await CreateAssignedLocationAsync();
        await Client.PutAsJsonAsync(
            $"/api/location-ciphers/{game.Id}/{location.Id}/hledani-magie",
            new UpsertLocationCipherDto("Tady zije vlci smecka"));

        var response = await Client.GetAsync(
            $"/api/location-ciphers/{game.Id}/{location.Id}/hledani-magie/pdf");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal($"ovcina-sifra-{location.Id}-hledani-magie.pdf", response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        await AssertPdfAsync(response);
    }

    [Fact]
    public async Task DownloadLocationPdf_ReturnsPdfWithDefinedCiphers()
    {
        var (game, location) = await CreateAssignedLocationAsync();
        await Client.PutAsJsonAsync(
            $"/api/location-ciphers/{game.Id}/{location.Id}/hledani-magie",
            new UpsertLocationCipherDto("Tady zije vlci smecka"));
        await Client.PutAsJsonAsync(
            $"/api/location-ciphers/{game.Id}/{location.Id}/lezeni",
            new UpsertLocationCipherDto("Vylez po skale"));

        var response = await Client.GetAsync(
            $"/api/location-ciphers/{game.Id}/{location.Id}/pdf");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        await AssertPdfAsync(response);
    }

    [Fact]
    public async Task DownloadLocationPdf_WithoutDefinedCiphers_ReturnsBadRequest()
    {
        var (game, location) = await CreateAssignedLocationAsync();

        var response = await Client.GetAsync(
            $"/api/location-ciphers/{game.Id}/{location.Id}/pdf");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_UpdatesExistingCipherInsteadOfDuplicating()
    {
        var (game, location) = await CreateAssignedLocationAsync();

        await Client.PutAsJsonAsync(
            $"/api/location-ciphers/{game.Id}/{location.Id}/sesty-smysl",
            new UpsertLocationCipherDto("První zpráva"));
        var response = await Client.PutAsJsonAsync(
            $"/api/location-ciphers/{game.Id}/{location.Id}/sesty-smysl",
            new UpsertLocationCipherDto("Druhá zpráva"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
        var ciphers = await db.LocationCiphers
            .Where(c => c.GameId == game.Id && c.LocationId == location.Id && c.Skill == AdventuringSkill.SestySmysl)
            .ToListAsync();
        var cipher = Assert.Single(ciphers);
        Assert.Equal("XOXDRUHAZPRAVAXOX", cipher.CipherText);
    }

    [Fact]
    public async Task Put_TooLongForLezeni_ReturnsBadRequest()
    {
        var (game, location) = await CreateAssignedLocationAsync();

        var response = await Client.PutAsJsonAsync(
            $"/api/location-ciphers/{game.Id}/{location.Id}/lezeni",
            new UpsertLocationCipherDto(new string('A', 73)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_RawMessageOverColumnLimit_ReturnsBadRequest()
    {
        var (game, location) = await CreateAssignedLocationAsync();

        var response = await Client.PutAsJsonAsync(
            $"/api/location-ciphers/{game.Id}/{location.Id}/hledani-magie",
            new UpsertLocationCipherDto(new string('.', 501) + "A"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_QuestTiedWithoutQuest_ReturnsBadRequest()
    {
        var (game, location) = await CreateAssignedLocationAsync();

        var response = await Client.PostAsJsonAsync("/api/location-ciphers",
            new LocationCipherCreateDto(game.Id, location.Id, AdventuringSkill.HledaniMagie,
                CipherTier.QuestTied, CipherContentType.Info, "Questová stopa"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_EmptyWrappedCipherText_ReturnsBadRequest()
    {
        var (game, location) = await CreateAssignedLocationAsync();

        var response = await Client.PostAsJsonAsync("/api/location-ciphers",
            new LocationCipherCreateDto(game.Id, location.Id, AdventuringSkill.HledaniMagie,
                CipherTier.Micro, CipherContentType.Info, "Odhalený text", CipherText: "XOXXOX"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_QuestNotLinkedToLocation_ReturnsBadRequest()
    {
        var (game, location) = await CreateAssignedLocationAsync();
        var questId = await CreateQuestWithoutLocationAsync(game.Id);

        var response = await Client.PutAsJsonAsync(
            $"/api/location-ciphers/{game.Id}/{location.Id}/prohledavani",
            new UpsertLocationCipherDto("Otevri pytlik", questId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Delete_RemovesCipher()
    {
        var (game, location) = await CreateAssignedLocationAsync();
        await Client.PutAsJsonAsync(
            $"/api/location-ciphers/{game.Id}/{location.Id}/znalost-bytosti",
            new UpsertLocationCipherDto("Nic tu neni"));

        using var admin = CreateRoleClient("Admin");
        var response = await admin.DeleteAsync(
            $"/api/location-ciphers/{game.Id}/{location.Id}/znalost-bytosti");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var slots = await Client.GetFromJsonAsync<List<LocationCipherSlotDto>>(
            $"/api/location-ciphers/{game.Id}/{location.Id}");
        Assert.Null(slots!.Single(s => s.SkillSlug == "znalost-bytosti").Cipher);
    }

    [Fact]
    public async Task BulkImport_AdditivelyUpsertsByGameLocationAndSkill()
    {
        var (game, location) = await CreateAssignedLocationAsync();
        using var admin = CreateRoleClient("Admin");

        var first = await admin.PostAsJsonAsync("/api/location-ciphers/bulk-import",
            new LocationCipherBulkImportDto(game.Id,
            [
                CreateCipher(game.Id, location.Id, AdventuringSkill.HledaniMagie, "První stopa"),
                CreateCipher(game.Id, location.Id, AdventuringSkill.Lezeni, "Vylez po skalce"),
                CreateCipher(game.Id, location.Id, AdventuringSkill.SestySmysl, "Něco tu cítíš")
            ]));
        first.EnsureSuccessStatusCode();

        var second = await admin.PostAsJsonAsync("/api/location-ciphers/bulk-import",
            new LocationCipherBulkImportDto(game.Id,
            [
                CreateCipher(game.Id, location.Id, AdventuringSkill.HledaniMagie, "Změněná stopa"),
                CreateCipher(game.Id, location.Id, AdventuringSkill.Lezeni, "Změněné lezení"),
                CreateCipher(game.Id, location.Id, AdventuringSkill.ZnalostBytosti, "Tohle je bazilišek")
            ]));
        second.EnsureSuccessStatusCode();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
        var rows = await db.LocationCiphers
            .Where(c => c.GameId == game.Id && c.LocationId == location.Id)
            .OrderBy(c => c.Skill)
            .ToListAsync();

        Assert.Equal(4, rows.Count);
        Assert.Contains(rows, c => c.Skill == AdventuringSkill.HledaniMagie && c.RevealText == "Změněná stopa");
        Assert.Contains(rows, c => c.Skill == AdventuringSkill.SestySmysl && c.RevealText == "Něco tu cítíš");
    }

    [Fact]
    public async Task ClaimVoucher_SetsClaimedAndRejectsSecondClaim()
    {
        var (game, location) = await CreateAssignedLocationAsync();
        var characterId = await CreateAssignedCharacterAsync(game.Id);
        var create = await Client.PostAsJsonAsync("/api/location-ciphers",
            new LocationCipherCreateDto(
                game.Id,
                location.Id,
                AdventuringSkill.Prohledavani,
                CipherTier.StandardVoucher,
                CipherContentType.Keyword,
                "Řekni heslo knihovníkovi",
                LibraryKeyword: "tajné-heslo",
                LibraryReward: "Knihovní odměna"));
        create.EnsureSuccessStatusCode();

        var claim = await Client.PostAsJsonAsync(
            $"/api/library-vouchers/claim?gameId={game.Id}",
            new CipherClaimRequestDto("tajne-heslo", true, characterId));
        claim.EnsureSuccessStatusCode();
        var result = await claim.Content.ReadFromJsonAsync<CipherClaimResultDto>();

        Assert.NotNull(result);
        Assert.True(result!.Success);
        Assert.Equal("Knihovní odměna", result.Reward);

        var secondClaim = await Client.PostAsJsonAsync(
            $"/api/library-vouchers/claim?gameId={game.Id}",
            new CipherClaimRequestDto("tajne-heslo", true, characterId));
        secondClaim.EnsureSuccessStatusCode();
        var secondResult = await secondClaim.Content.ReadFromJsonAsync<CipherClaimResultDto>();

        Assert.NotNull(secondResult);
        Assert.False(secondResult!.Success);
        Assert.Equal("Heslo již bylo uplatněno", secondResult.Reason);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
        var cipher = await db.LocationCiphers.SingleAsync(c => c.GameId == game.Id && c.LibraryKeyword == "TAJNEHESLO");
        Assert.True(cipher.IsClaimed);
        Assert.Equal(characterId, cipher.ClaimedByCharacterId);
        Assert.NotNull(cipher.ClaimedAtUtc);
    }

    [Fact]
    public async Task ClaimVoucher_UnknownKeywordReturnsUnknown()
    {
        var (game, _) = await CreateAssignedLocationAsync();
        var characterId = await CreateAssignedCharacterAsync(game.Id);

        var claim = await Client.PostAsJsonAsync(
            $"/api/library-vouchers/claim?gameId={game.Id}",
            new CipherClaimRequestDto("neexistuje", true, characterId));
        claim.EnsureSuccessStatusCode();
        var result = await claim.Content.ReadFromJsonAsync<CipherClaimResultDto>();

        Assert.NotNull(result);
        Assert.False(result!.Success);
        Assert.Equal("Heslo neznámé", result.Reason);
    }

    [Fact]
    public async Task DeleteAndBulkImport_ForOrganizer_ReturnForbidden()
    {
        var (game, location) = await CreateAssignedLocationAsync();
        var create = await Client.PostAsJsonAsync("/api/location-ciphers",
            new LocationCipherCreateDto(game.Id, location.Id, AdventuringSkill.HledaniMagie,
                CipherTier.Micro, CipherContentType.Info, "Jen admin mazání"));
        create.EnsureSuccessStatusCode();
        var cipher = await create.Content.ReadFromJsonAsync<LocationCipherDetailDto>();

        var delete = await Client.DeleteAsync($"/api/location-ciphers/{cipher!.Id}");
        var bulk = await Client.PostAsJsonAsync("/api/location-ciphers/bulk-import",
            new LocationCipherBulkImportDto(game.Id,
            [
                CreateCipher(game.Id, location.Id, AdventuringSkill.Prohledavani, "Hromadně")
            ]));

        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, bulk.StatusCode);
    }

    [Fact]
    public async Task Create_DuplicateLibraryKeywordInGameReturnsBadRequest()
    {
        var (game, location) = await CreateAssignedLocationAsync();
        var first = await Client.PostAsJsonAsync("/api/location-ciphers",
            new LocationCipherCreateDto(game.Id, location.Id, AdventuringSkill.HledaniMagie,
                CipherTier.StandardVoucher, CipherContentType.Keyword, "První heslo",
                LibraryKeyword: "stejne", LibraryReward: "První"));
        first.EnsureSuccessStatusCode();

        var duplicate = await Client.PostAsJsonAsync("/api/location-ciphers",
            new LocationCipherCreateDto(game.Id, location.Id, AdventuringSkill.Prohledavani,
                CipherTier.StandardVoucher, CipherContentType.Keyword, "Druhé heslo",
                LibraryKeyword: "stejné", LibraryReward: "Druhá"));

        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
    }

    [Fact]
    public async Task LocationCipherEndpoints_RequireAuthentication()
    {
        using var unauthenticated = Factory.CreateClient();

        var ciphers = await unauthenticated.GetAsync("/api/location-ciphers/by-game/1");
        var vouchers = await unauthenticated.GetAsync("/api/library-vouchers?gameId=1");

        Assert.Equal(HttpStatusCode.Unauthorized, ciphers.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, vouchers.StatusCode);
    }

    private async Task<(GameDetailDto Game, LocationDetailDto Location)> CreateAssignedLocationAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto($"Šifrová hra {suffix}", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        gameResponse.EnsureSuccessStatusCode();
        var game = (await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>())!;

        var locationResponse = await Client.PostAsJsonAsync("/api/locations",
            new CreateLocationDto($"Vlčí jeskyně {suffix}", LocationKind.Wilderness, 49.5m, 17.1m));
        locationResponse.EnsureSuccessStatusCode();
        var location = (await locationResponse.Content.ReadFromJsonAsync<LocationDetailDto>())!;

        var assignResponse = await Client.PostAsJsonAsync("/api/locations/by-game",
            new GameLocationDto(game.Id, location.Id));
        assignResponse.EnsureSuccessStatusCode();

        return (game, location);
    }

    private static LocationCipherCreateDto CreateCipher(
        int gameId,
        int locationId,
        AdventuringSkill skill,
        string revealText) =>
        new(gameId, locationId, skill, CipherTier.Micro, CipherContentType.Info, revealText);

    private async Task<int> CreateAssignedCharacterAsync(int gameId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var character = new Character
        {
            Name = $"Hrdina {suffix}",
            IsPlayedCharacter = true,
            ExternalPersonId = Random.Shared.Next(100_000, 999_999)
        };
        db.Characters.Add(character);
        await db.SaveChangesAsync();
        db.CharacterAssignments.Add(new CharacterAssignment
        {
            CharacterId = character.Id,
            GameId = gameId,
            ExternalPersonId = character.ExternalPersonId!.Value,
            IsActive = true
        });
        await db.SaveChangesAsync();
        return character.Id;
    }

    private HttpClient CreateRoleClient(params string[] roles)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwt.CreateToken(Factory.Services, roles));
        return client;
    }

    private async Task<int> CreateLocationQuestAsync(int gameId, int locationId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
        var quest = new Quest
        {
            Name = "Vlčí stopa",
            QuestType = QuestType.Location,
            GameId = gameId
        };
        db.Quests.Add(quest);
        await db.SaveChangesAsync();
        db.QuestLocationLinks.Add(new QuestLocationLink { QuestId = quest.Id, LocationId = locationId });
        await db.SaveChangesAsync();
        return quest.Id;
    }

    private async Task<int> CreateQuestWithoutLocationAsync(int gameId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
        var quest = new Quest
        {
            Name = "Jiný quest",
            QuestType = QuestType.Location,
            GameId = gameId
        };
        db.Quests.Add(quest);
        await db.SaveChangesAsync();
        return quest.Id;
    }

    private static async Task AssertPdfAsync(HttpResponseMessage response)
    {
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 1_000);
        Assert.Equal((byte)'%', bytes[0]);
        Assert.Equal((byte)'P', bytes[1]);
        Assert.Equal((byte)'D', bytes[2]);
        Assert.Equal((byte)'F', bytes[3]);
    }
}
