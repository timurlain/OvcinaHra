using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OvcinaHra.Shared.Domain.Entities;

namespace OvcinaHra.Api.Data.Configurations;

public class CraftingSkillRequirementConfiguration : IEntityTypeConfiguration<CraftingSkillRequirement>
{
    public void Configure(EntityTypeBuilder<CraftingSkillRequirement> builder)
    {
        builder.HasKey(e => new { e.CraftingRecipeId, e.GameSkillId });
        builder.HasOne(e => e.CraftingRecipe)
            .WithMany(r => r.SkillRequirements)
            .HasForeignKey(e => e.CraftingRecipeId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(e => e.GameSkill)
            .WithMany(s => s.CraftingRequirements)
            .HasForeignKey(e => e.GameSkillId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
