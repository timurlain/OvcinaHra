using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Api.Data.Configurations;

public class LocationCipherConfiguration : IEntityTypeConfiguration<LocationCipher>
{
    public void Configure(EntityTypeBuilder<LocationCipher> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.SkillKey).HasConversion<string>().HasMaxLength(40);
        builder.Property(e => e.MessageRaw).IsRequired().HasMaxLength(500);
        builder.Property(e => e.MessageNormalized).IsRequired().HasMaxLength(80);

        builder.HasOne(e => e.Game).WithMany().HasForeignKey(e => e.GameId);
        builder.HasOne(e => e.Location).WithMany().HasForeignKey(e => e.LocationId);
        builder.HasOne(e => e.Quest).WithMany().HasForeignKey(e => e.QuestId).OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(e => new { e.GameId, e.LocationId, e.SkillKey }).IsUnique();
        builder.HasIndex(e => new { e.GameId, e.LocationId });

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_LocationCipher_MessageNormalized_NotEmpty",
                "char_length(\"MessageNormalized\") > 0");
            t.HasCheckConstraint("CK_LocationCipher_MessageNormalized_MaxBySkill",
                $"(\"SkillKey\" = '{CipherSkillKey.Lezeni}' AND char_length(\"MessageNormalized\") <= 72) OR (\"SkillKey\" <> '{CipherSkillKey.Lezeni}' AND char_length(\"MessageNormalized\") <= 74)");
        });
    }
}
