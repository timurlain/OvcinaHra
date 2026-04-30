using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using OvcinaHra.Api.Tests.Fixtures;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Tests.Endpoints;

public class OrganizerRoleEndpointTests(PostgresFixture postgres) : IntegrationTestBase(postgres), IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task BulkAssign_CreatesAssignmentForEveryGameTimeSlot()
    {
        var (game, npc, slots) = await CreateGameWithNpcAndSlotsAsync(slotCount: 2);

        var response = await Client.PostAsJsonAsync(
            $"/api/games/{game.Id}/organizer-role-assignments/bulk",
            new BulkOrganizerRoleAssignmentDto(npc.Id, 501, "Pavel Dospělý", "pavel@example.com", "celá hra"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<BulkOrganizerRoleAssignmentResultDto>())!;
        Assert.Equal(2, result.CreatedCount);
        Assert.Equal(0, result.UpdatedCount);
        Assert.Equal(slots.Select(s => s.Id).Order(), result.Assignments.Select(a => a.GameTimeSlotId).Order());

        var matrix = await LoadMatrixAsync(game.Id);
        Assert.Equal(2, matrix.TimeSlots.Count);
        Assert.Single(matrix.Npcs);
        Assert.All(matrix.Assignments, a =>
        {
            Assert.Equal(npc.Id, a.NpcId);
            Assert.Equal(501, a.PersonId);
            Assert.Equal("Pavel Dospělý", a.PersonName);
        });
    }

    [Fact]
    public async Task BulkAssign_UpdatesExistingNpcSlotAssignments()
    {
        var (game, npc, slots) = await CreateGameWithNpcAndSlotsAsync(slotCount: 2);
        var firstSlot = slots[0];

        var initial = await Client.PutAsJsonAsync(
            $"/api/games/{game.Id}/organizer-role-assignments/slots/{firstSlot.Id}/npcs/{npc.Id}",
            new UpsertOrganizerRoleAssignmentDto(501, "Pavel Dospělý", "pavel@example.com"));
        Assert.Equal(HttpStatusCode.OK, initial.StatusCode);

        var response = await Client.PostAsJsonAsync(
            $"/api/games/{game.Id}/organizer-role-assignments/bulk",
            new BulkOrganizerRoleAssignmentDto(npc.Id, 777, "Jana Dospělá", "jana@example.com"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<BulkOrganizerRoleAssignmentResultDto>())!;
        Assert.Equal(1, result.CreatedCount);
        Assert.Equal(1, result.UpdatedCount);

        var matrix = await LoadMatrixAsync(game.Id);
        Assert.Equal(2, matrix.Assignments.Count);
        Assert.All(matrix.Assignments, a => Assert.Equal(777, a.PersonId));
    }

    [Fact]
    public async Task UpsertSlotAssignment_AllowsSameAdultAcrossMultipleNpcRolesInSameSlot()
    {
        var game = await CreateGameAsync();
        var slot = await CreateSlotAsync(game.Id, hour: 10);
        var merchant = await CreateNpcAsync("Kupec", NpcRole.Merchant);
        var monster = await CreateNpcAsync("Vlkodlak", NpcRole.Monster);
        await AssignNpcAsync(game.Id, merchant.Id);
        await AssignNpcAsync(game.Id, monster.Id);

        var dto = new UpsertOrganizerRoleAssignmentDto(501, "Pavel Dospělý", "pavel@example.com");
        var first = await Client.PutAsJsonAsync(
            $"/api/games/{game.Id}/organizer-role-assignments/slots/{slot.Id}/npcs/{merchant.Id}",
            dto);
        var second = await Client.PutAsJsonAsync(
            $"/api/games/{game.Id}/organizer-role-assignments/slots/{slot.Id}/npcs/{monster.Id}",
            dto);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var matrix = await LoadMatrixAsync(game.Id);
        Assert.Equal(2, matrix.Assignments.Count);
        Assert.All(matrix.Assignments, a =>
        {
            Assert.Equal(slot.Id, a.GameTimeSlotId);
            Assert.Equal(501, a.PersonId);
        });
    }

    [Fact]
    public async Task UpsertSlotAssignment_ReplacesExistingPersonForNpcSlot()
    {
        var (game, npc, slots) = await CreateGameWithNpcAndSlotsAsync(slotCount: 1);
        var slot = slots[0];

        await Client.PutAsJsonAsync(
            $"/api/games/{game.Id}/organizer-role-assignments/slots/{slot.Id}/npcs/{npc.Id}",
            new UpsertOrganizerRoleAssignmentDto(501, "Pavel Dospělý", "pavel@example.com"));
        var response = await Client.PutAsJsonAsync(
            $"/api/games/{game.Id}/organizer-role-assignments/slots/{slot.Id}/npcs/{npc.Id}",
            new UpsertOrganizerRoleAssignmentDto(777, "Jana Dospělá", "jana@example.com"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var matrix = await LoadMatrixAsync(game.Id);
        var assignment = Assert.Single(matrix.Assignments);
        Assert.Equal(777, assignment.PersonId);
        Assert.Equal("Jana Dospělá", assignment.PersonName);
    }

    [Fact]
    public async Task DeleteSlotAssignment_RemovesOnlySelectedSlotAssignment()
    {
        var (game, npc, slots) = await CreateGameWithNpcAndSlotsAsync(slotCount: 2);
        await Client.PostAsJsonAsync(
            $"/api/games/{game.Id}/organizer-role-assignments/bulk",
            new BulkOrganizerRoleAssignmentDto(npc.Id, 501, "Pavel Dospělý", "pavel@example.com"));

        var response = await Client.DeleteAsync(
            $"/api/games/{game.Id}/organizer-role-assignments/slots/{slots[0].Id}/npcs/{npc.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var matrix = await LoadMatrixAsync(game.Id);
        var assignment = Assert.Single(matrix.Assignments);
        Assert.Equal(slots[1].Id, assignment.GameTimeSlotId);
    }

    [Fact]
    public async Task DeleteGameNpc_RemovesOrganizerRoleAssignmentsForThatNpc()
    {
        var (game, npc, _) = await CreateGameWithNpcAndSlotsAsync(slotCount: 1);
        await Client.PostAsJsonAsync(
            $"/api/games/{game.Id}/organizer-role-assignments/bulk",
            new BulkOrganizerRoleAssignmentDto(npc.Id, 501, "Pavel Dospělý", "pavel@example.com"));

        var response = await Client.DeleteAsync($"/api/npcs/game-npc/{game.Id}/{npc.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var matrix = await LoadMatrixAsync(game.Id);
        Assert.Empty(matrix.Assignments);
    }

    [Fact]
    public async Task BulkAssign_NonexistentGame_ReturnsNotFoundBeforePayloadValidation()
    {
        var response = await Client.PostAsJsonAsync(
            "/api/games/999999999/organizer-role-assignments/bulk",
            new BulkOrganizerRoleAssignmentDto(1, 0, "", null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("Hra neexistuje", problem.Title);
        Assert.Equal("Požadovaná hra nebyla nalezena.", problem.Detail);
    }

    [Fact]
    public async Task BulkAssign_GameWithoutTimeSlots_ReturnsPolishedProblemDetails()
    {
        var game = await CreateGameAsync();
        var npc = await CreateNpcAsync("Bez slotů", NpcRole.Monster);
        await AssignNpcAsync(game.Id, npc.Id);

        var response = await Client.PostAsJsonAsync(
            $"/api/games/{game.Id}/organizer-role-assignments/bulk",
            new BulkOrganizerRoleAssignmentDto(npc.Id, 501, "Pavel Dospělý", "pavel@example.com"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("Hra nemá časové sloty", problem.Title);
        Assert.Equal("Nejdřív vytvořte časové bloky v harmonogramu.", problem.Detail);
    }

    [Fact]
    public async Task UpsertSlotAssignment_RejectsTooLongPersonFieldsWithProblemDetails()
    {
        var (game, npc, slots) = await CreateGameWithNpcAndSlotsAsync(slotCount: 1);
        var tooLongName = new string('A', 201);

        var response = await Client.PutAsJsonAsync(
            $"/api/games/{game.Id}/organizer-role-assignments/slots/{slots[0].Id}/npcs/{npc.Id}",
            new UpsertOrganizerRoleAssignmentDto(501, tooLongName, "pavel@example.com"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("Jméno dospělého je příliš dlouhé", problem.Title);
        Assert.Contains("200", problem.Detail ?? "");
    }

    [Fact]
    public async Task GetMine_ReturnsPrimaryRoleAndTimetableForCurrentUserEmail()
    {
        var game = await CreateGameAsync();
        var firstSlot = await CreateSlotAsync(game.Id, hour: 10);
        var secondSlot = await CreateSlotAsync(game.Id, hour: 11);
        var merchant = await CreateNpcAsync("Kupec Pavel", NpcRole.Merchant);
        var monster = await CreateNpcAsync("Vlkodlak", NpcRole.Monster);
        await AssignNpcAsync(game.Id, merchant.Id);
        await AssignNpcAsync(game.Id, monster.Id);

        await Client.PutAsJsonAsync(
            $"/api/games/{game.Id}/organizer-role-assignments/slots/{firstSlot.Id}/npcs/{merchant.Id}",
            new UpsertOrganizerRoleAssignmentDto(501, "Test Player", "TEST@OVCINA.CZ", "stánek"));
        await Client.PutAsJsonAsync(
            $"/api/games/{game.Id}/organizer-role-assignments/slots/{secondSlot.Id}/npcs/{merchant.Id}",
            new UpsertOrganizerRoleAssignmentDto(501, "Test Player", "test@ovcina.cz"));
        await Client.PutAsJsonAsync(
            $"/api/games/{game.Id}/organizer-role-assignments/slots/{firstSlot.Id}/npcs/{monster.Id}",
            new UpsertOrganizerRoleAssignmentDto(501, "Test Player", "test@ovcina.cz"));

        var mine = await LoadMineAsync(game.Id);

        Assert.Equal("test@ovcina.cz", mine.UserEmail);
        Assert.Equal(501, mine.PersonId);
        Assert.NotNull(mine.PrimaryRole);
        Assert.Equal(merchant.Id, mine.PrimaryRole.NpcId);
        Assert.Equal(2, mine.PrimaryRole.AssignmentCount);
        Assert.Equal(2, mine.TimeSlots.Count);
        Assert.Equal(2, mine.TimeSlots.Single(s => s.SlotId == firstSlot.Id).Assignments.Count);
        Assert.Single(mine.TimeSlots.Single(s => s.SlotId == secondSlot.Id).Assignments);
    }

    [Fact]
    public async Task GetMine_LeavesSlotsVisibleWhenUserHasNoAssignments()
    {
        var game = await CreateGameAsync();
        var slot = await CreateSlotAsync(game.Id, hour: 10);
        var npc = await CreateNpcAsync("Kupec bez hráče", NpcRole.Merchant);
        await AssignNpcAsync(game.Id, npc.Id);
        await Client.PutAsJsonAsync(
            $"/api/games/{game.Id}/organizer-role-assignments/slots/{slot.Id}/npcs/{npc.Id}",
            new UpsertOrganizerRoleAssignmentDto(777, "Někdo jiný", "other@example.test"));

        var mine = await LoadMineAsync(game.Id);

        Assert.Null(mine.PrimaryRole);
        Assert.Null(mine.PersonId);
        var visibleSlot = Assert.Single(mine.TimeSlots);
        Assert.Equal(slot.Id, visibleSlot.SlotId);
        Assert.Empty(visibleSlot.Assignments);
    }

    [Fact]
    public async Task BulkFillRemaining_FillsOnlySlotsWhereAdultHasNoAssignmentUsingDefaultRole()
    {
        var game = await CreateGameAsync();
        var slots = new List<GameTimeSlotDto>();
        for (var i = 0; i < 5; i++)
        {
            slots.Add(await CreateSlotAsync(game.Id, hour: 10 + i));
        }
        var eldari = await CreateNpcAsync("Eldari", NpcRole.Story);
        var goblinChief = await CreateNpcAsync("Goblin chief", NpcRole.Monster);
        await AssignNpcAsync(game.Id, eldari.Id);
        await AssignNpcAsync(game.Id, goblinChief.Id);
        await Client.PutAsJsonAsync(
            $"/api/games/{game.Id}/organizer-role-assignments/slots/{slots[0].Id}/npcs/{eldari.Id}",
            new UpsertOrganizerRoleAssignmentDto(501, "Pavel Dospělý", "pavel@example.com"));
        await Client.PutAsJsonAsync(
            $"/api/games/{game.Id}/organizer-role-assignments/slots/{slots[2].Id}/npcs/{goblinChief.Id}",
            new UpsertOrganizerRoleAssignmentDto(501, "Pavel Dospělý", "pavel@example.com"));

        var response = await Client.PostAsJsonAsync(
            "/api/organizer-roles/bulk-fill-remaining",
            new BulkFillRemainingOrganizerRoleDto(game.Id, 501));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<BulkFillRemainingOrganizerRoleResultDto>())!;
        Assert.Equal(3, result.InsertedCount);
        Assert.Equal(2, result.SkippedCount);
        Assert.Equal(eldari.Id, result.DefaultNpcId);

        var matrix = await LoadMatrixAsync(game.Id);
        var pavelAssignments = matrix.Assignments
            .Where(a => a.PersonId == 501)
            .ToDictionary(a => a.GameTimeSlotId);
        Assert.Equal(5, pavelAssignments.Count);
        Assert.Equal(eldari.Id, pavelAssignments[slots[0].Id].NpcId);
        Assert.Equal(goblinChief.Id, pavelAssignments[slots[2].Id].NpcId);
        Assert.All(
            new[] { slots[1].Id, slots[3].Id, slots[4].Id },
            slotId => Assert.Equal(eldari.Id, pavelAssignments[slotId].NpcId));
    }

    [Fact]
    public async Task BulkFillRemaining_AdultWithoutAssignmentsReturnsProblemDetails()
    {
        var (game, _, _) = await CreateGameWithNpcAndSlotsAsync(slotCount: 2);

        var response = await Client.PostAsJsonAsync(
            "/api/organizer-roles/bulk-fill-remaining",
            new BulkFillRemainingOrganizerRoleDto(game.Id, 501));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("Chybí výchozí role", problem.Title);
        Assert.Contains("výchozí role", problem.Detail ?? "");
    }

    [Fact]
    public async Task ExportCsv_ReturnsUtf8BomSemicolonCsvForAdultSlots()
    {
        var game = await CreateGameAsync("Balinova pozvánka");
        var firstSlot = await CreateSlotAsync(game.Id, hour: 10, GameTimePhase.Early);
        var secondSlot = await CreateSlotAsync(game.Id, hour: 11, GameTimePhase.Midgame);
        var merchant = await CreateNpcAsync("Kupec Čenda", NpcRole.Merchant);
        var monster = await CreateNpcAsync("Vlkodlak", NpcRole.Monster);
        await AssignNpcAsync(game.Id, merchant.Id);
        await AssignNpcAsync(game.Id, monster.Id);
        await Client.PutAsJsonAsync(
            $"/api/games/{game.Id}/organizer-role-assignments/slots/{firstSlot.Id}/npcs/{merchant.Id}",
            new UpsertOrganizerRoleAssignmentDto(501, "Pavel Dospělý", "pavel@example.com"));
        await Client.PutAsJsonAsync(
            $"/api/games/{game.Id}/organizer-role-assignments/slots/{firstSlot.Id}/npcs/{monster.Id}",
            new UpsertOrganizerRoleAssignmentDto(501, "Pavel Dospělý", "pavel@example.com"));
        await Client.PutAsJsonAsync(
            $"/api/games/{game.Id}/organizer-role-assignments/slots/{secondSlot.Id}/npcs/{merchant.Id}",
            new UpsertOrganizerRoleAssignmentDto(777, "Jana Žlutá", "jana@example.com"));

        var response = await Client.GetAsync($"/api/organizer-roles/export?gameId={game.Id}&format=csv");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("utf-8", (response.Content.Headers.ContentType?.CharSet ?? "").ToLowerInvariant());
        Assert.Equal(
            $"OrganizerRoles_Balinova-pozvanka_{DateTime.Today:yyyy-MM-dd}.csv",
            response.Content.Headers.ContentDisposition?.FileNameStar
                ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()));
        var csv = Encoding.UTF8.GetString(bytes[Encoding.UTF8.GetPreamble().Length..]);
        var lines = csv.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(5, lines.Length);
        Assert.Equal("\"Dospělý\";\"E-mail\";\"Slot\";\"Začátek\";\"Konec\";\"Fáze\";\"NPC role\";\"Role tag\"", lines[0]);
        Assert.Contains(
            "\"Pavel Dospělý\";\"pavel@example.com\";\"Rozvoj hry: Rok 1247, 1.5.2026 12:00\";\"1.5.2026 12:00\";\"1.5.2026 13:00\";\"Rozvoj hry\";\"Kupec Čenda, Vlkodlak\";\"Obchodník, Nestvůra\"",
            lines);
        Assert.Contains(
            "\"Jana Žlutá\";\"jana@example.com\";\"Rozvoj hry: Rok 1247, 1.5.2026 12:00\";\"1.5.2026 12:00\";\"1.5.2026 13:00\";\"Rozvoj hry\";\"\";\"\"",
            lines);
    }

    private async Task<(GameDetailDto Game, NpcDetailDto Npc, List<GameTimeSlotDto> Slots)> CreateGameWithNpcAndSlotsAsync(int slotCount)
    {
        var game = await CreateGameAsync();
        var npc = await CreateNpcAsync("Gobliní král", NpcRole.Monster);
        await AssignNpcAsync(game.Id, npc.Id);

        var slots = new List<GameTimeSlotDto>();
        for (var i = 0; i < slotCount; i++)
        {
            slots.Add(await CreateSlotAsync(game.Id, 10 + i));
        }

        return (game, npc, slots);
    }

    private async Task<GameDetailDto> CreateGameAsync(string name = "Rozpis rolí")
    {
        var response = await Client.PostAsJsonAsync(
            "/api/games",
            new CreateGameDto(name, 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<GameDetailDto>())!;
    }

    private async Task<NpcDetailDto> CreateNpcAsync(string name, NpcRole role)
    {
        var response = await Client.PostAsJsonAsync(
            "/api/npcs",
            new CreateNpcDto(name, role, "Role pro organizátorský rozpis"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<NpcDetailDto>())!;
    }

    private async Task AssignNpcAsync(int gameId, int npcId)
    {
        var response = await Client.PostAsJsonAsync("/api/npcs/game-npc", new CreateGameNpcDto(gameId, npcId));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private async Task<GameTimeSlotDto> CreateSlotAsync(
        int gameId,
        int hour,
        GameTimePhase stage = GameTimePhase.Start)
    {
        var response = await Client.PostAsJsonAsync(
            "/api/timeline/slots",
            new CreateGameTimeSlotDto(
                GameId: gameId,
                StartTime: new DateTime(2026, 5, 1, hour, 0, 0, DateTimeKind.Utc),
                DurationHours: 1,
                InGameYear: 1247,
                Stage: stage));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<GameTimeSlotDto>())!;
    }

    private async Task<OrganizerRoleMatrixDto> LoadMatrixAsync(int gameId)
    {
        var matrix = await Client.GetFromJsonAsync<OrganizerRoleMatrixDto>(
            $"/api/games/{gameId}/organizer-role-assignments");
        return matrix!;
    }

    private async Task<OrganizerRoleMeDto> LoadMineAsync(int gameId)
    {
        var response = await Client.GetAsync($"/api/organizer-roles/me?gameId={gameId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<OrganizerRoleMeDto>())!;
    }
}
