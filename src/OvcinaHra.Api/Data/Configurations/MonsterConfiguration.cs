using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OvcinaHra.Shared.Domain.Entities;

namespace OvcinaHra.Api.Data.Configurations;

public class MonsterConfiguration : IEntityTypeConfiguration<Monster>
{
    public void Configure(EntityTypeBuilder<Monster> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).IsRequired().HasMaxLength(200);
        builder.HasIndex(e => e.Name).IsUnique();
        builder.Property(e => e.MonsterType).HasConversion<string>().HasMaxLength(30);

        builder.OwnsOne(e => e.Stats, s =>
        {
            s.Property(p => p.Attack).HasColumnName("stat_attack");
            s.Property(p => p.Defense).HasColumnName("stat_defense");
            s.Property(p => p.Health).HasColumnName("stat_health");
        });

        builder.Property<NpgsqlTypes.NpgsqlTsVector>("SearchVector")
            .HasColumnType("tsvector")
            .HasComputedColumnSql(
                "to_tsvector('simple', coalesce(\"Name\", '') || ' ' || coalesce(\"Abilities\", '') || ' ' || coalesce(\"AiBehavior\", ''))",
                stored: true);

        builder.HasIndex("SearchVector").HasMethod("GIN");
    }
}

public class MonsterTagLinkConfiguration : IEntityTypeConfiguration<MonsterTagLink>
{
    public void Configure(EntityTypeBuilder<MonsterTagLink> builder)
    {
        builder.HasKey(e => new { e.MonsterId, e.TagId });
        builder.HasOne(e => e.Monster).WithMany(m => m.MonsterTags).HasForeignKey(e => e.MonsterId);
        builder.HasOne(e => e.Tag).WithMany(t => t.MonsterTags).HasForeignKey(e => e.TagId);
    }
}

public class MonsterLootConfiguration : IEntityTypeConfiguration<MonsterLoot>
{
    public void Configure(EntityTypeBuilder<MonsterLoot> builder)
    {
        builder.HasKey(e => new { e.MonsterId, e.ItemId, e.GameId });
        builder.HasOne(e => e.Monster).WithMany(m => m.MonsterLoots).HasForeignKey(e => e.MonsterId);
        builder.HasOne(e => e.Item).WithMany(i => i.MonsterLoots).HasForeignKey(e => e.ItemId);
        builder.HasOne(e => e.Game).WithMany(g => g.MonsterLoots).HasForeignKey(e => e.GameId);
    }
}
