using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OvcinaHra.Shared.Domain.Entities;

namespace OvcinaHra.Api.Data.Configurations;

public class SecretStashConfiguration : IEntityTypeConfiguration<SecretStash>
{
    public void Configure(EntityTypeBuilder<SecretStash> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).IsRequired().HasMaxLength(200);
        builder.HasIndex(e => new { e.GameId, e.Name }).IsUnique();
        builder.HasOne(e => e.Location).WithMany(l => l.SecretStashes).HasForeignKey(e => e.LocationId);
        builder.HasOne(e => e.Game).WithMany(g => g.SecretStashes).HasForeignKey(e => e.GameId);

        // Max 3 secret stashes per location per game — enforced at API level
        // (CHECK constraints on count require triggers in PostgreSQL)
    }
}

public class TreasureQuestConfiguration : IEntityTypeConfiguration<TreasureQuest>
{
    public void Configure(EntityTypeBuilder<TreasureQuest> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Title).IsRequired().HasMaxLength(300);
        builder.Property(e => e.Difficulty).HasConversion<string>().HasMaxLength(20);
        builder.HasOne(e => e.Location).WithMany(l => l.TreasureQuests).HasForeignKey(e => e.LocationId);
        builder.HasOne(e => e.SecretStash).WithMany(s => s.TreasureQuests).HasForeignKey(e => e.SecretStashId);
        builder.HasOne(e => e.Game).WithMany(g => g.TreasureQuests).HasForeignKey(e => e.GameId);

        // Exactly one of LocationId or SecretStashId must be set
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_TreasureQuest_LocationOrStash",
            "(\"LocationId\" IS NOT NULL) <> (\"SecretStashId\" IS NOT NULL)"));
    }
}

public class TreasureItemConfiguration : IEntityTypeConfiguration<TreasureItem>
{
    public void Configure(EntityTypeBuilder<TreasureItem> builder)
    {
        builder.HasKey(e => new { e.TreasureQuestId, e.ItemId });
        builder.HasOne(e => e.TreasureQuest).WithMany(t => t.TreasureItems).HasForeignKey(e => e.TreasureQuestId);
        builder.HasOne(e => e.Item).WithMany(i => i.TreasureItems).HasForeignKey(e => e.ItemId);
    }
}
