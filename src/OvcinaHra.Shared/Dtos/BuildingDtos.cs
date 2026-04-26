namespace OvcinaHra.Shared.Dtos;

public record BuildingListDto(
    int Id, string Name, string? Description, string? Notes,
    int? LocationId, string? LocationName, bool IsPrebuilt,
    string? ImagePath = null, string? ImageUrl = null,
    // Issue #142 — per-game construction recipe summary. Populated by the
    // by-game endpoint with a compact "3× ingrediencí · 50 zlaťáků · 1 dovednost"
    // string for display in the grid; catalog endpoint leaves null.
    string? RecipeSummary = null,
    // Issue #208 — building's own "build recipe" pair (cost → effect). Null
    // CostMoney renders as "Zdarma" pill; null Effect renders as muted
    // "Bez efektu" placeholder.
    int? CostMoney = null,
    string? Effect = null,
    // "Použito v" cluster counts surfaced on tile/card-row footers + Karta
    // hero. CraftingRecipesCount = number of CraftingRecipes requiring this
    // building. GamesCount = number of games using this building via
    // GameBuilding link. Both projected server-side to avoid N+1 lookups.
    int CraftingRecipesCount = 0,
    int GamesCount = 0);

public record BuildingDetailDto(
    int Id, string Name, string? Description, string? Notes, string? ImagePath,
    int? LocationId, string? LocationName, bool IsPrebuilt,
    string? ImageUrl = null,
    int? CostMoney = null,
    string? Effect = null,
    int CraftingRecipesCount = 0,
    int GamesCount = 0);

public record CreateBuildingDto(
    string Name,
    string? Description = null, string? Notes = null,
    int? LocationId = null, bool IsPrebuilt = false,
    int? CostMoney = null, string? Effect = null);

public record UpdateBuildingDto(
    string Name, string? Description, string? Notes,
    int? LocationId, bool IsPrebuilt,
    int? CostMoney = null, string? Effect = null);

// Game assignment
public record GameBuildingDto(int GameId, int BuildingId);
