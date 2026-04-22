using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OvcinaHra.Shared.Domain.Entities;

namespace OvcinaHra.Api.Data.Configurations;

public class PersonalQuestConfiguration : IEntityTypeConfiguration<PersonalQuest>
{
    public void Configure(EntityTypeBuilder<PersonalQuest> b)
    {
        b.HasKey(e => e.Id);
        b.Property(e => e.Name).IsRequired().HasMaxLength(200);
        b.HasIndex(e => e.Name).IsUnique();
        b.Property(e => e.Description).HasMaxLength(500);
        b.Property(e => e.Difficulty).HasConversion<string>().HasMaxLength(20);
        b.ToTable(t => t.HasCheckConstraint(
            "CK_PersonalQuest_XpCost_NonNegative",
            "\"XpCost\" >= 0"));
    }
}
