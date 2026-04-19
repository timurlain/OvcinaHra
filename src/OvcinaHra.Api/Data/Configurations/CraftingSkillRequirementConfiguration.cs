using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OvcinaHra.Shared.Domain.Entities;

namespace OvcinaHra.Api.Data.Configurations;

public class CraftingSkillRequirementConfiguration : IEntityTypeConfiguration<CraftingSkillRequirement>
{
    public void Configure(EntityTypeBuilder<CraftingSkillRequirement> builder)
    {
        builder.HasKey(e => new { e.CraftingRecipeId, e.SkillId });
        builder.HasOne(e => e.CraftingRecipe).WithMany(r => r.SkillRequirements).HasForeignKey(e => e.CraftingRecipeId);
        builder.HasOne(e => e.Skill).WithMany(s => s.CraftingRequirements).HasForeignKey(e => e.SkillId);
    }
}
