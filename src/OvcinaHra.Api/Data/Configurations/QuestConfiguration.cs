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
        builder.HasOne(e => e.ParentQuest).WithMany(q => q.ChildQuests).HasForeignKey(e => e.ParentQuestId);
        builder.HasOne(e => e.Game).WithMany(g => g.Quests).HasForeignKey(e => e.GameId);

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
