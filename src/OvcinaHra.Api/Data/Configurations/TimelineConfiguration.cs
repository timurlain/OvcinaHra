using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OvcinaHra.Shared.Domain.Entities;

namespace OvcinaHra.Api.Data.Configurations;

public class GameTimeSlotConfiguration : IEntityTypeConfiguration<GameTimeSlot>
{
    public void Configure(EntityTypeBuilder<GameTimeSlot> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasOne(e => e.BattlefieldBonus).WithMany(b => b.TimeSlots).HasForeignKey(e => e.BattlefieldBonusId);
        builder.HasOne(e => e.Game).WithMany(g => g.TimeSlots).HasForeignKey(e => e.GameId);
    }
}

public class BattlefieldBonusConfiguration : IEntityTypeConfiguration<BattlefieldBonus>
{
    public void Configure(EntityTypeBuilder<BattlefieldBonus> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).HasMaxLength(200);
        builder.HasOne(e => e.Game).WithMany(g => g.BattlefieldBonuses).HasForeignKey(e => e.GameId);
    }
}
