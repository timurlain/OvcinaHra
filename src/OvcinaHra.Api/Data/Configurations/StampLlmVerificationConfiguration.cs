using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OvcinaHra.Shared.Domain.Entities;

namespace OvcinaHra.Api.Data.Configurations;

public class StampLlmVerificationConfiguration : IEntityTypeConfiguration<StampLlmVerification>
{
    public void Configure(EntityTypeBuilder<StampLlmVerification> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.OrganizerUserId).IsRequired().HasMaxLength(200);
        builder.Property(e => e.OrganizerName).IsRequired().HasMaxLength(200);
        builder.Property(e => e.RawResponse).IsRequired().HasMaxLength(1000);
        // Mode discriminator. Default "verify" so existing rows backfill cleanly when the
        // column is added; new rows from /recognize set "recognize".
        builder.Property(e => e.Mode).IsRequired().HasMaxLength(20).HasDefaultValue(StampLlmVerification.ModeVerify);
        builder.HasOne(e => e.Location).WithMany().HasForeignKey(e => e.LocationId);
        builder.HasIndex(e => e.TimestampUtc);
        builder.HasIndex(e => e.LocationId);
        builder.HasIndex(e => e.Mode);
    }
}
