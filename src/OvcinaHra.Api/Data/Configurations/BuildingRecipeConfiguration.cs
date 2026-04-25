using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OvcinaHra.Shared.Domain.Entities;

namespace OvcinaHra.Api.Data.Configurations;

// Issue #142 — Building crafting cost. Mirrors CraftingConfiguration shape;
// kept in a separate file so the parallel domain isn't visually conflated
// with the Item-side crafting model.

public class BuildingRecipeConfiguration : IEntityTypeConfiguration<BuildingRecipe>
{
    public void Configure(EntityTypeBuilder<BuildingRecipe> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasOne(e => e.Game).WithMany(g => g.BuildingRecipes).HasForeignKey(e => e.GameId);
        builder.HasOne(e => e.OutputBuilding).WithMany(b => b.BuildingRecipesAsOutput).HasForeignKey(e => e.OutputBuildingId);
        builder.Property(e => e.IngredientNotes).HasMaxLength(2000);
    }
}

public class BuildingRecipeIngredientConfiguration : IEntityTypeConfiguration<BuildingRecipeIngredient>
{
    public void Configure(EntityTypeBuilder<BuildingRecipeIngredient> builder)
    {
        builder.HasKey(e => new { e.BuildingRecipeId, e.ItemId });
        builder.HasOne(e => e.BuildingRecipe).WithMany(r => r.Ingredients).HasForeignKey(e => e.BuildingRecipeId);
        builder.HasOne(e => e.Item).WithMany().HasForeignKey(e => e.ItemId);
    }
}

public class BuildingRecipePrerequisiteConfiguration : IEntityTypeConfiguration<BuildingRecipePrerequisite>
{
    public void Configure(EntityTypeBuilder<BuildingRecipePrerequisite> builder)
    {
        builder.HasKey(e => new { e.BuildingRecipeId, e.RequiredBuildingId });
        builder.HasOne(e => e.BuildingRecipe).WithMany(r => r.PrerequisiteBuildings).HasForeignKey(e => e.BuildingRecipeId);
        builder.HasOne(e => e.RequiredBuilding).WithMany(b => b.BuildingRecipesAsPrerequisite).HasForeignKey(e => e.RequiredBuildingId);
    }
}

public class BuildingRecipeSkillRequirementConfiguration : IEntityTypeConfiguration<BuildingRecipeSkillRequirement>
{
    public void Configure(EntityTypeBuilder<BuildingRecipeSkillRequirement> builder)
    {
        builder.HasKey(e => new { e.BuildingRecipeId, e.GameSkillId });
        builder.HasOne(e => e.BuildingRecipe).WithMany(r => r.SkillRequirements).HasForeignKey(e => e.BuildingRecipeId);
        // GameSkill side: no inverse navigation — keep the GameSkill entity
        // unchanged for this feature so we don't ripple into the skills domain.
        builder.HasOne(e => e.GameSkill).WithMany().HasForeignKey(e => e.GameSkillId);
    }
}
