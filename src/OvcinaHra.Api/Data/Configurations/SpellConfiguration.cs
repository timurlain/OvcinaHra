using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OvcinaHra.Shared.Domain.Entities;

namespace OvcinaHra.Api.Data.Configurations;

public class SpellConfiguration : IEntityTypeConfiguration<Spell>
{
    public void Configure(EntityTypeBuilder<Spell> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).IsRequired().HasMaxLength(100);
        builder.HasIndex(e => e.Name).IsUnique();

        builder.Property(e => e.School).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Effect).IsRequired().HasMaxLength(4000);
        builder.Property(e => e.Description).HasMaxLength(4000);
        builder.Property(e => e.ImagePath).HasMaxLength(500);

        // Full-text search — shadow property + GIN index. Mirrors Item/Location/Monster/Quest.
        builder.Property<NpgsqlTypes.NpgsqlTsVector>("SearchVector")
            .HasColumnType("tsvector")
            .HasComputedColumnSql(
                "to_tsvector('simple', coalesce(\"Name\", '') || ' ' || coalesce(\"Effect\", '') || ' ' || coalesce(\"Description\", ''))",
                stored: true);

        builder.HasIndex("SearchVector").HasMethod("GIN");
    }
}
