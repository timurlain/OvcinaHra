using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OvcinaHra.Shared.Domain.Entities;

namespace OvcinaHra.Api.Data.Configurations;

public class CraftingRecipeConfiguration : IEntityTypeConfiguration<CraftingRecipe>
{
    public void Configure(EntityTypeBuilder<CraftingRecipe> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasOne(e => e.Game).WithMany(g => g.CraftingRecipes).HasForeignKey(e => e.GameId);
        builder.HasOne(e => e.OutputItem).WithMany().HasForeignKey(e => e.OutputItemId);
        builder.HasOne(e => e.Location).WithMany().HasForeignKey(e => e.LocationId);
        builder.Property(e => e.IngredientNotes).HasMaxLength(2000);
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
