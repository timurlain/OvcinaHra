using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OvcinaHra.Shared.Domain.Entities;

namespace OvcinaHra.Api.Data.Configurations;

public class SkillBuildingRequirementConfiguration : IEntityTypeConfiguration<SkillBuildingRequirement>
{
    public void Configure(EntityTypeBuilder<SkillBuildingRequirement> builder)
    {
        builder.HasKey(e => new { e.SkillId, e.BuildingId });
        builder.HasOne(e => e.Skill).WithMany(s => s.BuildingRequirements).HasForeignKey(e => e.SkillId);
        builder.HasOne(e => e.Building).WithMany().HasForeignKey(e => e.BuildingId);
    }
}
