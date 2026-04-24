using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OvcinaHra.Shared.Domain.Entities;

namespace OvcinaHra.Api.Data.Configurations;

public class GameConfiguration : IEntityTypeConfiguration<Game>
{
    public void Configure(EntityTypeBuilder<Game> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);

        // Map bounding box — matches LocationConfiguration's Coordinates precision.
        builder.Property(e => e.BoundingBoxSwLat).HasPrecision(10, 7);
        builder.Property(e => e.BoundingBoxSwLng).HasPrecision(10, 7);
        builder.Property(e => e.BoundingBoxNeLat).HasPrecision(10, 7);
        builder.Property(e => e.BoundingBoxNeLng).HasPrecision(10, 7);
    }
}
