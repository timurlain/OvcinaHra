using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using OvcinaHra.E2E.Fixtures;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.E2E;

/// <summary>
/// Phase 9 — E2E flow for the skills domain.
///
/// Exercises the full user flow end-to-end via the same API that the UI drives:
/// catalog template create → per-game copy-on-assign → crafting recipe requirement →
/// persistence re-fetch. Follows the API-driven pattern of existing E2E tests (see
/// the class-level comment in <see cref="GameManagementTests"/>) because the E2E
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

        // 2) Create a skill template in the catalog — equivalent of /skills (Knihovna).
        var skillCreateResp = await client.PostAsJsonAsync("/api/skills",
            new CreateSkillRequest(
                Name: "Tichý úder",
                Category: SkillCategory.Class,
                ClassRestriction: PlayerClass.Thief,
                Effect: "Test — útok z úkrytu",
                RequirementNotes: null,
                RequiredBuildingIds: [building!.Id]));
        skillCreateResp.EnsureSuccessStatusCode();
        var template = await skillCreateResp.Content.ReadFromJsonAsync<SkillDto>();
        Assert.NotNull(template);
        Assert.Equal("Tichý úder", template.Name);
        Assert.Equal(SkillCategory.Class, template.Category);
        Assert.Equal(PlayerClass.Thief, template.ClassRestriction);
        Assert.Contains(building.Id, template.RequiredBuildingIds);

        // Template appears in the catalog listing — mirrors "row appears in grid".
        var catalog = await client.GetFromJsonAsync<List<SkillDto>>("/api/skills");
        Assert.NotNull(catalog);
        Assert.Contains(catalog, s => s.Id == template.Id && s.Name == "Tichý úder");

        // 3) Copy-on-assign: create a GameSkill from the template with per-game XP/Level.
        //    Equivalent of "Ze šablony" flow in the per-game page.
        var createGameSkillResp = await client.PostAsJsonAsync(
            $"/api/games/{game!.Id}/skills",
            new CreateGameSkillRequest(
                TemplateSkillId: template.Id,
                Name: template.Name,
                Category: template.Category,
                ClassRestriction: template.ClassRestriction,
                Effect: template.Effect,
                RequirementNotes: template.RequirementNotes,
                BuildingRequirementIds: template.RequiredBuildingIds.ToList(),
                XpCost: 10,
                LevelRequirement: 2));
        createGameSkillResp.EnsureSuccessStatusCode();
        var gameSkill = await createGameSkillResp.Content.ReadFromJsonAsync<GameSkillDto>();
        Assert.NotNull(gameSkill);
        Assert.Equal(template.Id, gameSkill.TemplateSkillId);
        Assert.Equal("Tichý úder", gameSkill.Name);
        Assert.Equal(SkillCategory.Class, gameSkill.Category);
        Assert.Equal(10, gameSkill.XpCost);
        Assert.Equal(2, gameSkill.LevelRequirement);

        var gameSkills = await client.GetFromJsonAsync<List<GameSkillDto>>(
            $"/api/games/{game.Id}/skills");
        Assert.NotNull(gameSkills);
        Assert.Single(gameSkills, gs => gs.Id == gameSkill.Id);

        // 4) Require the GameSkill in a crafting recipe — equivalent of the Recept
        //    block on the item edit dialog. Recipes reference GameSkill.Id now,
        //    not the template's Skill.Id.
        var recipeResp = await client.PostAsJsonAsync("/api/crafting",
            new CreateCraftingRecipeDto(
                GameId: game.Id,
                OutputItemId: item!.Id,
                LocationId: null,
                RequiredSkillIds: [gameSkill.Id]));
        recipeResp.EnsureSuccessStatusCode();
        var recipe = await recipeResp.Content.ReadFromJsonAsync<CraftingRecipeDetailDto>();
        Assert.NotNull(recipe);
        Assert.Contains(gameSkill.Id, recipe.RequiredSkillIds);

        // 5) Persistence check — round-trip fetch (mirrors "reload page, reopen item").
        var recipeReloaded = await client.GetFromJsonAsync<CraftingRecipeDetailDto>(
            $"/api/crafting/{recipe.Id}");
        Assert.NotNull(recipeReloaded);
        Assert.Equal(recipe.Id, recipeReloaded.Id);
        Assert.Equal(item.Id, recipeReloaded.OutputItemId);
        Assert.Contains(gameSkill.Id, recipeReloaded.RequiredSkillIds);

        var gameRecipes = await client.GetFromJsonAsync<List<CraftingRecipeListDto>>(
            $"/api/crafting/by-game/{game.Id}");
        Assert.NotNull(gameRecipes);
        Assert.Contains(gameRecipes, r => r.Id == recipe.Id && r.OutputItemId == item.Id);

        // Template is still unchanged after all downstream wiring — the Effect on
        // the game copy and the Effect on the template are independent.
        var templateAfter = await client.GetFromJsonAsync<SkillDto>($"/api/skills/{template.Id}");
        Assert.NotNull(templateAfter);
        Assert.Equal("Tichý úder", templateAfter.Name);
        Assert.Equal("Test — útok z úkrytu", templateAfter.Effect);
        Assert.Contains(building.Id, templateAfter.RequiredBuildingIds);
    }

    [Fact]
    public async Task GameSkill_CannotBeRemoved_WhenReferencedByRecipe()
    {
        await _fixture.CleanDatabaseAsync();
        var client = _fixture.ApiClient;
        var token = await _fixture.GetDevTokenAsync();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // Minimal seed: game + item + template + GameSkill copy
        var gameResp = await client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Guard hra", 31,
                new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 3)));
        var game = (await gameResp.Content.ReadFromJsonAsync<GameDetailDto>())!;

        var itemResp = await client.PostAsJsonAsync("/api/items",
            new CreateItemDto("Dýka", ItemType.Weapon, IsCraftable: true));
        var item = (await itemResp.Content.ReadFromJsonAsync<ItemDetailDto>())!;

        var templateResp = await client.PostAsJsonAsync("/api/skills",
            new CreateSkillRequest(
                Name: "Tichý krok",
                Category: SkillCategory.Class,
                ClassRestriction: PlayerClass.Thief,
                Effect: "E2E guard",
                RequirementNotes: null,
                RequiredBuildingIds: []));
        var template = (await templateResp.Content.ReadFromJsonAsync<SkillDto>())!;

        // Assign into the game (copy-on-assign) and reference from a recipe
        var gsResp = await client.PostAsJsonAsync(
            $"/api/games/{game.Id}/skills",
            new CreateGameSkillRequest(
                TemplateSkillId: template.Id,
                Name: template.Name,
                Category: template.Category,
                ClassRestriction: template.ClassRestriction,
                Effect: template.Effect,
                RequirementNotes: null,
                BuildingRequirementIds: [],
                XpCost: 5,
                LevelRequirement: null));
        var gameSkill = (await gsResp.Content.ReadFromJsonAsync<GameSkillDto>())!;

        await client.PostAsJsonAsync("/api/crafting",
            new CreateCraftingRecipeDto(game.Id, item.Id, null, [gameSkill.Id]));

        // Attempting to remove the GameSkill from the game should fail with Conflict —
        // mirrors what the UI surfaces when the user tries to remove an in-use skill.
        var removeResp = await client.DeleteAsync(
            $"/api/games/{game.Id}/skills/{gameSkill.Id}");
        Assert.Equal(System.Net.HttpStatusCode.Conflict, removeResp.StatusCode);

        // GameSkill survives
        var stillThere = await client.GetFromJsonAsync<List<GameSkillDto>>(
            $"/api/games/{game.Id}/skills");
        Assert.NotNull(stillThere);
        Assert.Contains(stillThere, gs => gs.Id == gameSkill.Id);

        // Because the recipe keeps the per-game copy alive, template DELETE is blocked
        // by the catalog's usage guard and surfaces Czech ProblemDetails.
        var templateDeleteResp = await client.DeleteAsync($"/api/skills/{template.Id}");
        Assert.Equal(System.Net.HttpStatusCode.Conflict, templateDeleteResp.StatusCode);

        var problem = await templateDeleteResp.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("Dovednost nelze smazat", problem.Title);
        Assert.Equal((int)System.Net.HttpStatusCode.Conflict, problem.Status);
        Assert.Contains("Šablona má kopie v 1 hrách.", problem.Detail);

        var afterTemplateDeleteBlocked = await client.GetFromJsonAsync<List<GameSkillDto>>(
            $"/api/games/{game.Id}/skills");
        Assert.NotNull(afterTemplateDeleteBlocked);
        var copy = Assert.Single(afterTemplateDeleteBlocked, gs => gs.Id == gameSkill.Id);
        Assert.Equal(template.Id, copy.TemplateSkillId);
        Assert.Equal("Tichý krok", copy.Name); // name preserved in the copy
    }
}
