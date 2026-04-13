using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OvcinaHra.Shared.Domain.Entities;

namespace OvcinaHra.Api.Data.Configurations;

public class GameEventConfiguration : IEntityTypeConfiguration<GameEvent>
{
    public void Configure(EntityTypeBuilder<GameEvent> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).IsRequired().HasMaxLength(300);
        builder.HasOne(e => e.Game).WithMany(g => g.GameEvents).HasForeignKey(e => e.GameId);
        builder.HasIndex(e => e.GameId);
        builder.HasQueryFilter(e => !e.IsDeleted);
    }
}

public class GameEventTimeSlotConfiguration : IEntityTypeConfiguration<GameEventTimeSlot>
{
    public void Configure(EntityTypeBuilder<GameEventTimeSlot> builder)
    {
        builder.HasKey(e => new { e.GameEventId, e.GameTimeSlotId });
        builder.HasOne(e => e.GameEvent).WithMany(ge => ge.EventTimeSlots).HasForeignKey(e => e.GameEventId);
        builder.HasOne(e => e.TimeSlot).WithMany(ts => ts.EventTimeSlots).HasForeignKey(e => e.GameTimeSlotId);
    }
}

public class GameEventLocationConfiguration : IEntityTypeConfiguration<GameEventLocation>
{
    public void Configure(EntityTypeBuilder<GameEventLocation> builder)
    {
        builder.HasKey(e => new { e.GameEventId, e.LocationId });
        builder.HasOne(e => e.GameEvent).WithMany(ge => ge.EventLocations).HasForeignKey(e => e.GameEventId);
        builder.HasOne(e => e.Location).WithMany(l => l.EventLocations).HasForeignKey(e => e.LocationId);
    }
}

public class GameEventQuestConfiguration : IEntityTypeConfiguration<GameEventQuest>
{
    public void Configure(EntityTypeBuilder<GameEventQuest> builder)
    {
        builder.HasKey(e => new { e.GameEventId, e.QuestId });
        builder.HasOne(e => e.GameEvent).WithMany(ge => ge.EventQuests).HasForeignKey(e => e.GameEventId);
        builder.HasOne(e => e.Quest).WithMany(q => q.EventQuests).HasForeignKey(e => e.QuestId);
    }
}

public class GameEventNpcConfiguration : IEntityTypeConfiguration<GameEventNpc>
{
    public void Configure(EntityTypeBuilder<GameEventNpc> builder)
    {
        builder.HasKey(e => new { e.GameEventId, e.NpcId });
        builder.Property(e => e.RoleInEvent).HasMaxLength(100);
        builder.HasOne(e => e.GameEvent).WithMany(ge => ge.EventNpcs).HasForeignKey(e => e.GameEventId);
        builder.HasOne(e => e.Npc).WithMany(n => n.EventNpcs).HasForeignKey(e => e.NpcId);
    }
}
