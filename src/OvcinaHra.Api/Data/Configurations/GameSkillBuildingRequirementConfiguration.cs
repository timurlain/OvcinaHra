using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OvcinaHra.Shared.Domain.Entities;

namespace OvcinaHra.Api.Data.Configurations;

public class GameSkillBuildingRequirementConfiguration : IEntityTypeConfiguration<GameSkillBuildingRequirement>
{
    public void Configure(EntityTypeBuilder<GameSkillBuildingRequirement> builder)
    {
        builder.HasKey(e => new { e.GameSkillId, e.BuildingId });
        builder.HasOne(e => e.GameSkill)
            .WithMany(s => s.BuildingRequirements)
            .HasForeignKey(e => e.GameSkillId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(e => e.Building).WithMany().HasForeignKey(e => e.BuildingId);
    }
}
