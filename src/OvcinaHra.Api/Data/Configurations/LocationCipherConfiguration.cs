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
        builder.Property(e => e.Skill).HasConversion<string>().HasMaxLength(40);
        builder.Property(e => e.Tier).HasConversion<string>().HasMaxLength(40);
        builder.Property(e => e.ContentType).HasConversion<string>().HasMaxLength(40);
        builder.Property(e => e.RevealText).IsRequired().HasMaxLength(500);
        builder.Property(e => e.CipherText).HasMaxLength(120);
        builder.Property(e => e.LibraryKeyword).HasMaxLength(50);
        builder.Property(e => e.LibraryReward).HasMaxLength(500);
        builder.Property(e => e.OrganizerNotes).HasMaxLength(1000);
        builder.Property(e => e.IsClaimed).HasDefaultValue(false);

        builder.HasOne(e => e.Game).WithMany().HasForeignKey(e => e.GameId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(e => e.Location).WithMany().HasForeignKey(e => e.LocationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(e => e.LinkedQuest).WithMany().HasForeignKey(e => e.LinkedQuestId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(e => e.ClaimedByCharacter).WithMany().HasForeignKey(e => e.ClaimedByCharacterId).OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(e => new { e.GameId, e.LocationId, e.Skill }).IsUnique();
        builder.HasIndex(e => new { e.GameId, e.LocationId });
        builder.HasIndex(e => new { e.GameId, e.LibraryKeyword })
            .IsUnique()
            .HasFilter("\"LibraryKeyword\" IS NOT NULL");

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_LocationCipher_RevealText_NotEmpty",
                "char_length(trim(\"RevealText\")) > 0");
            t.HasCheckConstraint("CK_LocationCipher_CipherText_Wrapped",
                "\"CipherText\" IS NULL OR (\"CipherText\" = upper(\"CipherText\") AND \"CipherText\" LIKE 'XOX%XOX')");
            t.HasCheckConstraint("CK_LocationCipher_Pytlik_HasStash",
                "\"ContentType\" <> 'Pytlik' OR \"LinkedStashNumber\" IS NOT NULL");
            t.HasCheckConstraint("CK_LocationCipher_ClaimedAt_WhenClaimed",
                "(\"IsClaimed\" = false AND \"ClaimedAtUtc\" IS NULL) OR (\"IsClaimed\" = true AND \"ClaimedAtUtc\" IS NOT NULL)");
        });
    }
}
