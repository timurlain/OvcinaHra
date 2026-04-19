using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OvcinaHra.Shared.Domain.Entities;

namespace OvcinaHra.Api.Data.Configurations;

public class SkillConfiguration : IEntityTypeConfiguration<Skill>
{
    public void Configure(EntityTypeBuilder<Skill> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).IsRequired().HasMaxLength(100);
        builder.HasIndex(e => e.Name).IsUnique();
        builder.Property(e => e.Category).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.ClassRestriction).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Effect).HasMaxLength(4000);
        builder.Property(e => e.RequirementNotes).HasMaxLength(1000);
        builder.Property(e => e.ImagePath).HasMaxLength(500);
    }
}
