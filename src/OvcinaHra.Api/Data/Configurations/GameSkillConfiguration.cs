using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OvcinaHra.Shared.Domain.Entities;

namespace OvcinaHra.Api.Data.Configurations;

public class GameSkillConfiguration : IEntityTypeConfiguration<GameSkill>
{
    public void Configure(EntityTypeBuilder<GameSkill> builder)
    {
        builder.HasKey(e => new { e.GameId, e.SkillId });
        builder.HasOne(e => e.Game).WithMany().HasForeignKey(e => e.GameId);
        builder.HasOne(e => e.Skill).WithMany(s => s.GameSkills).HasForeignKey(e => e.SkillId);
        builder.HasIndex(e => e.GameId);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_GameSkill_XpCost_NonNegative", "\"XpCost\" >= 0");
            t.HasCheckConstraint("CK_GameSkill_LevelRequirement_NonNegative", "\"LevelRequirement\" IS NULL OR \"LevelRequirement\" >= 0");
        });
    }
}
