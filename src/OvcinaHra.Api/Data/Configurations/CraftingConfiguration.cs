using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OvcinaHra.Shared.Domain.Entities;

namespace OvcinaHra.Api.Data.Configurations;

public class CraftingRecipeConfiguration : IEntityTypeConfiguration<CraftingRecipe>
{
    public void Configure(EntityTypeBuilder<CraftingRecipe> builder)
    {
        builder.HasKey(e => e.Id);
        // Issue #218 — GameId nullable (catalog templates have GameId == null).
        builder.HasOne(e => e.Game).WithMany(g => g.CraftingRecipes).HasForeignKey(e => e.GameId).IsRequired(false);
        builder.HasOne(e => e.OutputItem).WithMany().HasForeignKey(e => e.OutputItemId);
        builder.HasOne(e => e.Location).WithMany().HasForeignKey(e => e.LocationId);
        builder.Property(e => e.IngredientNotes).HasMaxLength(2000);

        // Issue #218 — recipe.Name (optional). 300-char ceiling matches Quest
        // / Building / Item naming columns elsewhere.
        builder.Property(e => e.Name).HasMaxLength(300);

        // Issue #218 — Category enum stored as string (HasConversion). Same
        // pattern as Quest.State + Building.State elsewhere in the schema.
        builder.Property(e => e.Category).HasConversion<string>().HasMaxLength(20);

        // Issue #218 — self-FK for template fork. ON DELETE SET NULL would be
        // safer than the EF default, but cascading is fine for Phase 1: per-
        // game forks are independent rows that survive their template's
        // deletion — DeleteBehavior.SetNull keeps them as orphans the user
        // can re-link or delete manually.
        builder.HasOne(e => e.TemplateRecipe)
            .WithMany()
            .HasForeignKey(e => e.TemplateRecipeId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class CraftingIngredientConfiguration : IEntityTypeConfiguration<CraftingIngredient>
{
    public void Configure(EntityTypeBuilder<CraftingIngredient> builder)
    {
        builder.HasKey(e => new { e.CraftingRecipeId, e.ItemId });
        builder.HasOne(e => e.CraftingRecipe).WithMany(r => r.Ingredients).HasForeignKey(e => e.CraftingRecipeId);
        builder.HasOne(e => e.Item).WithMany(i => i.CraftingIngredients).HasForeignKey(e => e.ItemId);
    }
}

public class CraftingBuildingRequirementConfiguration : IEntityTypeConfiguration<CraftingBuildingRequirement>
{
    public void Configure(EntityTypeBuilder<CraftingBuildingRequirement> builder)
    {
        builder.HasKey(e => new { e.CraftingRecipeId, e.BuildingId });
        builder.HasOne(e => e.CraftingRecipe).WithMany(r => r.BuildingRequirements).HasForeignKey(e => e.CraftingRecipeId);
        builder.HasOne(e => e.Building).WithMany(b => b.CraftingRequirements).HasForeignKey(e => e.BuildingId);
    }
}
