using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OvcinaHra.Shared.Domain.Entities;

namespace OvcinaHra.Api.Data.Configurations;

public class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    public void Configure(EntityTypeBuilder<Item> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).IsRequired().HasMaxLength(200);
        builder.HasIndex(e => e.Name).IsUnique();
        builder.Property(e => e.ItemType).HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.PhysicalForm).HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.Note).HasMaxLength(2000);

        builder.OwnsOne(e => e.ClassRequirements, cr =>
        {
            cr.Property(p => p.Warrior).HasColumnName("req_warrior");
            cr.Property(p => p.Archer).HasColumnName("req_archer");
            cr.Property(p => p.Mage).HasColumnName("req_mage");
            cr.Property(p => p.Thief).HasColumnName("req_thief");
        });

        builder.Property<NpgsqlTypes.NpgsqlTsVector>("SearchVector")
            .HasColumnType("tsvector")
            .HasComputedColumnSql(
                "to_tsvector('simple', coalesce(\"Name\", '') || ' ' || coalesce(\"Effect\", ''))",
                stored: true);

        builder.HasIndex("SearchVector").HasMethod("GIN");
    }
}
