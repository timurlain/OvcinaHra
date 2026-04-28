using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OvcinaHra.Shared.Domain.Entities;

namespace OvcinaHra.Api.Data.Configurations;

public class QuestConfiguration : IEntityTypeConfiguration<Quest>
{
    public void Configure(EntityTypeBuilder<Quest> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).IsRequired().HasMaxLength(300);
        builder.Property(e => e.QuestType).HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.State).HasConversion<string>().HasMaxLength(20);
        builder.HasOne(e => e.ParentQuest).WithMany(q => q.ChildQuests).HasForeignKey(e => e.ParentQuestId);
        builder.HasOne(e => e.Game).WithMany(g => g.Quests).HasForeignKey(e => e.GameId).IsRequired(false);
        builder.HasOne(e => e.TimeSlot)
            .WithMany(ts => ts.Quests)
            .HasForeignKey(e => e.TimeSlotId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Property<NpgsqlTypes.NpgsqlTsVector>("SearchVector")
            .HasColumnType("tsvector")
            .HasComputedColumnSql(
                "to_tsvector('simple', coalesce(\"Name\", '') || ' ' || coalesce(\"Description\", '') || ' ' || coalesce(\"FullText\", ''))",
                stored: true);

        builder.HasIndex("SearchVector").HasMethod("GIN");
    }
}

public class QuestTagLinkConfiguration : IEntityTypeConfiguration<QuestTagLink>
{
    public void Configure(EntityTypeBuilder<QuestTagLink> builder)
    {
        builder.HasKey(e => new { e.QuestId, e.TagId });
        builder.HasOne(e => e.Quest).WithMany(q => q.QuestTags).HasForeignKey(e => e.QuestId);
        builder.HasOne(e => e.Tag).WithMany(t => t.QuestTags).HasForeignKey(e => e.TagId);
    }
}

public class QuestLocationLinkConfiguration : IEntityTypeConfiguration<QuestLocationLink>
{
    public void Configure(EntityTypeBuilder<QuestLocationLink> builder)
    {
        builder.HasKey(e => new { e.QuestId, e.LocationId });
        builder.HasOne(e => e.Quest).WithMany(q => q.QuestLocations).HasForeignKey(e => e.QuestId);
        builder.HasOne(e => e.Location).WithMany(l => l.QuestLocations).HasForeignKey(e => e.LocationId);
    }
}

public class QuestEncounterConfiguration : IEntityTypeConfiguration<QuestEncounter>
{
    public void Configure(EntityTypeBuilder<QuestEncounter> builder)
    {
        builder.HasKey(e => new { e.QuestId, e.MonsterId });
        builder.HasOne(e => e.Quest).WithMany(q => q.QuestEncounters).HasForeignKey(e => e.QuestId);
        builder.HasOne(e => e.Monster).WithMany(m => m.QuestEncounters).HasForeignKey(e => e.MonsterId);
    }
}

public class QuestRewardConfiguration : IEntityTypeConfiguration<QuestReward>
{
    public void Configure(EntityTypeBuilder<QuestReward> builder)
    {
        builder.HasKey(e => new { e.QuestId, e.ItemId });
        builder.HasOne(e => e.Quest).WithMany(q => q.QuestRewards).HasForeignKey(e => e.QuestId);
        builder.HasOne(e => e.Item).WithMany(i => i.QuestRewards).HasForeignKey(e => e.ItemId);
    }
}

/// <summary>
/// Issue #214 — ordered location waypoints inside a quest. Surrogate
/// PK so reordering doesn't cascade-delete waypoint rows the way a
/// (QuestId, Order) composite PK would. Plain unique index on
/// (QuestId, Order) keeps two waypoints from claiming the same step.
/// </summary>
public class QuestWaypointConfiguration : IEntityTypeConfiguration<QuestWaypoint>
{
    public void Configure(EntityTypeBuilder<QuestWaypoint> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Label).HasMaxLength(120);
        builder.HasOne(e => e.Quest)
            .WithMany(q => q.QuestWaypoints)
            .HasForeignKey(e => e.QuestId)
            .OnDelete(DeleteBehavior.Cascade);
        // Restrict on Location — deleting a location while a quest still
        // references it as a waypoint should fail loudly, not silently
        // sever the path. The location editor should refuse the delete.
        builder.HasOne(e => e.Location)
            .WithMany()
            .HasForeignKey(e => e.LocationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.QuestId, e.Order }).IsUnique();
    }
}
