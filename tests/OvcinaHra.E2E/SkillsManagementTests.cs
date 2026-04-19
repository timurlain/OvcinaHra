using System.Net.Http.Json;
using OvcinaHra.E2E.Fixtures;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.E2E;

/// <summary>
/// Phase 9 — E2E flow for the skills domain.
///
/// Exercises the full user flow end-to-end via the same API that the UI drives:
/// catalog create → game activation → crafting recipe requirement → persistence
/// re-fetch. Follows the API-driven pattern of existing E2E tests (see the
/// class-level comment in <see cref="GameManagementTests"/>) because the E2E
/// harness hosts the API through WebApplicationFactory and does not serve the
/// Blazor WASM client for browser-level navigation.
/// </summary>
[Collection("E2E")]
public class SkillsManagementTests
{
    private readonly E2EFixture _fixture;

    public SkillsManagementTests(E2EFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Skill_Catalog_To_Game_To_Recipe_Flow_Persists()
    {
        await _fixture.CleanDatabaseAsync();
        var client = _fixture.ApiClient;

        // Auth — mirrors what the UI does by storing the dev token in localStorage
        var token = await _fixture.GetDevTokenAsync();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // 1) Seed: create Game, Building, output Item (the fast-path equivalent
        //    of UI-driving unrelated screens).
        var gameResp = await client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Skills E2E hra", 30,
                new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        gameResp.EnsureSuccessStatusCode();
        var game = await gameResp.Content.ReadFromJsonAsync<GameDetailDto>();
        Assert.NotNull(game);

        var buildingResp = await client.PostAsJsonAsync("/api/buildings",
            new CreateBuildingDto("Zlodějský úkryt", Description: "Test building for skill requirement"));
        buildingResp.EnsureSuccessStatusCode();
        var building = await buildingResp.Content.ReadFromJsonAsync<BuildingDetailDto>();
        Assert.NotNull(building);

        var itemResp = await client.PostAsJsonAsync("/api/items",
            new CreateItemDto("Tichý dýka", ItemType.Weapon,
                Effect: "Test output item for recipe", IsCraftable: true));
        itemResp.EnsureSuccessStatusCode();
        var item = await itemResp.Content.ReadFromJsonAsync<ItemDetailDto>();
        Assert.NotNull(item);

        // 2) Create a skill in the catalog — equivalent of /skills page + popup.
        var skillCreateResp = await client.PostAsJsonAsync("/api/skills",
            new CreateSkillRequest(
                Name: "Tichý úder",
                ClassRestriction: PlayerClass.Thief,
                Effect: "Test — útok z úkrytu",
                RequirementNotes: null,
                RequiredBuildingIds: [building!.Id]));
        skillCreateResp.EnsureSuccessStatusCode();
        var skill = await skillCreateResp.Content.ReadFromJsonAsync<SkillDto>();
        Assert.NotNull(skill);
        Assert.Equal("Tichý úder", skill.Name);
        Assert.Equal(PlayerClass.Thief, skill.ClassRestriction);
        Assert.Contains(building.Id, skill.RequiredBuildingIds);

        // Assert it shows up in the catalog listing — equivalent of "row appears in grid".
        var catalog = await client.GetFromJsonAsync<List<SkillDto>>("/api/skills");
        Assert.NotNull(catalog);
        Assert.Contains(catalog, s => s.Id == skill.Id && s.Name == "Tichý úder");

        // 3) Activate the skill in the game — equivalent of /games/{id}/skills + dropdown.
        var upsertResp = await client.PutAsJsonAsync(
            $"/api/games/{game!.Id}/skills/{skill.Id}",
            new UpsertGameSkillRequest(XpCost: 10, LevelRequirement: 2));
        upsertResp.EnsureSuccessStatusCode();

        var gameSkills = await client.GetFromJsonAsync<List<GameSkillDto>>(
            $"/api/games/{game.Id}/skills");
        Assert.NotNull(gameSkills);
        var activated = Assert.Single(gameSkills, gs => gs.SkillId == skill.Id);
        Assert.Equal(10, activated.XpCost);
        Assert.Equal(2, activated.LevelRequirement);
        Assert.Equal("Tichý úder", activated.SkillName);

        // 4) Require the skill in a crafting recipe — equivalent of the Recept
        //    block on the item edit dialog. Create the recipe (output = seeded item,
        //    no ingredients, recipe requires the freshly activated skill).
        var recipeResp = await client.PostAsJsonAsync("/api/crafting",
            new CreateCraftingRecipeDto(
                GameId: game.Id,
                OutputItemId: item!.Id,
                LocationId: null,
                RequiredSkillIds: [skill.Id]));
        recipeResp.EnsureSuccessStatusCode();
        var recipe = await recipeResp.Content.ReadFromJsonAsync<CraftingRecipeDetailDto>();
        Assert.NotNull(recipe);
        Assert.Contains(skill.Id, recipe.RequiredSkillIds);

        // 5) Persistence check — round-trip fetch (mirrors "reload page, reopen item").
        //    Fetch via both the detail endpoint and the game-scoped list to verify
        //    the requirement survived the write.
        var recipeReloaded = await client.GetFromJsonAsync<CraftingRecipeDetailDto>(
            $"/api/crafting/{recipe.Id}");
        Assert.NotNull(recipeReloaded);
        Assert.Equal(recipe.Id, recipeReloaded.Id);
        Assert.Equal(item.Id, recipeReloaded.OutputItemId);
        Assert.Contains(skill.Id, recipeReloaded.RequiredSkillIds);

        var gameRecipes = await client.GetFromJsonAsync<List<CraftingRecipeListDto>>(
            $"/api/crafting/by-game/{game.Id}");
        Assert.NotNull(gameRecipes);
        Assert.Contains(gameRecipes, r => r.Id == recipe.Id && r.OutputItemId == item.Id);

        // Skill catalog still shows the skill with its building requirement
        // after all downstream wiring — guards against accidental cascade side effects.
        var skillAfter = await client.GetFromJsonAsync<SkillDto>($"/api/skills/{skill.Id}");
        Assert.NotNull(skillAfter);
        Assert.Equal("Tichý úder", skillAfter.Name);
        Assert.Contains(building.Id, skillAfter.RequiredBuildingIds);
    }

    [Fact]
    public async Task Skill_CannotBeDeleted_WhenReferencedByRecipe()
    {
        await _fixture.CleanDatabaseAsync();
        var client = _fixture.ApiClient;
        var token = await _fixture.GetDevTokenAsync();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // Minimal seed: game + item + skill
        var gameResp = await client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Guard hra", 31,
                new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 3)));
        var game = (await gameResp.Content.ReadFromJsonAsync<GameDetailDto>())!;

        var itemResp = await client.PostAsJsonAsync("/api/items",
            new CreateItemDto("Dýka", ItemType.Weapon, IsCraftable: true));
        var item = (await itemResp.Content.ReadFromJsonAsync<ItemDetailDto>())!;

        var skillResp = await client.PostAsJsonAsync("/api/skills",
            new CreateSkillRequest("Tichý krok", PlayerClass.Thief, "E2E guard", null, []));
        var skill = (await skillResp.Content.ReadFromJsonAsync<SkillDto>())!;

        // Activate in game and reference from recipe
        await client.PutAsJsonAsync($"/api/games/{game.Id}/skills/{skill.Id}",
            new UpsertGameSkillRequest(5, null));
        await client.PostAsJsonAsync("/api/crafting",
            new CreateCraftingRecipeDto(game.Id, item.Id, null, [skill.Id]));

        // Attempting to delete the skill should fail with Conflict —
        // mirrors what the UI surfaces when the user tries to remove an in-use skill.
        var deleteResp = await client.DeleteAsync($"/api/skills/{skill.Id}");
        Assert.Equal(System.Net.HttpStatusCode.Conflict, deleteResp.StatusCode);

        // And the skill survives
        var skillAfter = await client.GetFromJsonAsync<SkillDto>($"/api/skills/{skill.Id}");
        Assert.NotNull(skillAfter);
        Assert.Equal("Tichý krok", skillAfter.Name);
    }
}
