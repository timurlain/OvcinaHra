using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OvcinaHra.Shared.Domain.Entities;

namespace OvcinaHra.Api.Data.Configurations;

public class GameSkillConfiguration : IEntityTypeConfiguration<GameSkill>
{
    public void Configure(EntityTypeBuilder<GameSkill> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Name).IsRequired().HasMaxLength(100);
        builder.Property(e => e.ClassRestriction).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Effect).HasMaxLength(1000);
        builder.Property(e => e.RequirementNotes).HasMaxLength(1000);
        builder.Property(e => e.ImagePath).HasMaxLength(500);

        builder.HasOne(e => e.Game)
            .WithMany()
            .HasForeignKey(e => e.GameId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Skill)
            .WithMany(s => s.GameSkills)
            .HasForeignKey(e => e.TemplateSkillId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(e => new { e.GameId, e.Name }).IsUnique();
        builder.HasIndex(e => new { e.GameId, e.TemplateSkillId })
            .IsUnique()
            .HasFilter("\"TemplateSkillId\" IS NOT NULL");

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_GameSkill_XpCost_NonNegative", "\"XpCost\" >= 0");
            t.HasCheckConstraint("CK_GameSkill_LevelRequirement_NonNegative", "\"LevelRequirement\" IS NULL OR \"LevelRequirement\" >= 0");
        });
    }
}
