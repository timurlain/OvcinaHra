using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OvcinaHra.Shared.Domain.Entities;

namespace OvcinaHra.Api.Data.Configurations;

public class KingdomConfiguration : IEntityTypeConfiguration<Kingdom>
{
    public void Configure(EntityTypeBuilder<Kingdom> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).IsRequired().HasMaxLength(100);
        builder.Property(e => e.HexColor).HasMaxLength(7);
        builder.Property(e => e.BadgeImageUrl).HasMaxLength(500);
        builder.Property(e => e.Description).HasMaxLength(2000);
        builder.HasIndex(e => e.Name).IsUnique();
    }
}
