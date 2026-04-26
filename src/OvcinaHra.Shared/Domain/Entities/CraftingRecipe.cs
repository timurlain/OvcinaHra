using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Shared.Domain.Entities;

public class CraftingRecipe
{
    public int Id { get; set; }

    // Issue #218 — Recipes catalog port. GameId is now nullable: null = catalog
    // template, set = per-game record (forked from a template OR od-nuly).
    public int? GameId { get; set; }

    // Issue #218 — recipes carry a name distinct from the output item's name
    // ("Hojivý lektvar — varianta s šalvějí" producing the catalog "Hojivý
    // lektvar"). Optional so legacy data without a name still loads — reads
    // fall back to OutputItem.Name in projections.
    public string? Name { get; set; }

    // Issue #218 — when this is a per-game fork of a catalog template, points
    // back to the source template (also a CraftingRecipe row, with GameId
    // null). Null on catalog templates and on od-nuly per-game records.
    public int? TemplateRecipeId { get; set; }
    public CraftingRecipe? TemplateRecipe { get; set; }

    // Issue #218 — 4-value taxonomy (Budova / Lektvar / Artefakt / Ostatní).
    // Stored as string in PostgreSQL so EnumIsDefined validation works at
    // the API layer and DB rows stay grep-friendly during data audits.
    public RecipeCategory Category { get; set; } = RecipeCategory.Ostatni;

    public int OutputItemId { get; set; }
    public int? LocationId { get; set; }

    // Free-text note rendered after the ingredient list (issue #121).
    // Example use: "Byliny — 3× stejný druh" — constraints on ingredient
    // combinations that can't be encoded in the structured ingredient rows.
    // Not tied to any one ingredient; belongs at the recipe level.
    public string? IngredientNotes { get; set; }

    public Game? Game { get; set; }
    public Item OutputItem { get; set; } = null!;
    public Location? Location { get; set; }
    public ICollection<CraftingIngredient> Ingredients { get; set; } = [];
    public ICollection<CraftingBuildingRequirement> BuildingRequirements { get; set; } = [];
    public ICollection<CraftingSkillRequirement> SkillRequirements { get; set; } = [];
}
