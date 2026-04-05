using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OvcinaHra.Shared.Domain.Entities;

namespace OvcinaHra.Api.Data.Configurations;

public class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).IsRequired().HasMaxLength(200);
        builder.HasIndex(e => e.Name).IsUnique();
        builder.Property(e => e.LocationKind).HasConversion<string>().HasMaxLength(30);

        builder.OwnsOne(e => e.Coordinates, c =>
        {
            c.Property(p => p.Latitude).HasColumnName("latitude").HasPrecision(10, 7);
            c.Property(p => p.Longitude).HasColumnName("longitude").HasPrecision(10, 7);
        });

        builder.Property<NpgsqlTypes.NpgsqlTsVector>("SearchVector")
            .HasColumnType("tsvector")
            .HasComputedColumnSql(
                "to_tsvector('simple', coalesce(\"Name\", '') || ' ' || coalesce(\"Description\", '') || ' ' || coalesce(\"NpcInfo\", '') || ' ' || coalesce(\"SetupNotes\", ''))",
                stored: true);

        builder.HasIndex("SearchVector").HasMethod("GIN");
    }
}
