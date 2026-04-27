using System.Net;
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
        Assert.Equal("TADYZIJEVLCISMECKA", cipher!.MessageNormalized);
        Assert.Equal("XOXTADYZIJEVLCISMECKAXOX", cipher.EncodedPreview);
        Assert.Equal(questId, cipher.QuestId);
        Assert.Equal("Vlčí stopa", cipher.QuestName);
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
            .Where(c => c.GameId == game.Id && c.LocationId == location.Id && c.SkillKey == CipherSkillKey.SestySmysl)
            .ToListAsync();
        var cipher = Assert.Single(ciphers);
        Assert.Equal("DRUHAZPRAVA", cipher.MessageNormalized);
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

        var response = await Client.DeleteAsync(
            $"/api/location-ciphers/{game.Id}/{location.Id}/znalost-bytosti");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var slots = await Client.GetFromJsonAsync<List<LocationCipherSlotDto>>(
            $"/api/location-ciphers/{game.Id}/{location.Id}");
        Assert.Null(slots!.Single(s => s.SkillSlug == "znalost-bytosti").Cipher);
    }

    private async Task<(GameDetailDto Game, LocationDetailDto Location)> CreateAssignedLocationAsync()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Šifrová hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        gameResponse.EnsureSuccessStatusCode();
        var game = (await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>())!;

        var locationResponse = await Client.PostAsJsonAsync("/api/locations",
            new CreateLocationDto("Vlčí jeskyně", LocationKind.Wilderness, 49.5m, 17.1m));
        locationResponse.EnsureSuccessStatusCode();
        var location = (await locationResponse.Content.ReadFromJsonAsync<LocationDetailDto>())!;

        var assignResponse = await Client.PostAsJsonAsync("/api/locations/by-game",
            new GameLocationDto(game.Id, location.Id));
        assignResponse.EnsureSuccessStatusCode();

        return (game, location);
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
}
