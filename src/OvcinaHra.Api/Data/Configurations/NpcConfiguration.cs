using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OvcinaHra.Shared.Domain.Entities;

namespace OvcinaHra.Api.Data.Configurations;

public class NpcConfiguration : IEntityTypeConfiguration<Npc>
{
    public void Configure(EntityTypeBuilder<Npc> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Role).HasConversion<string>().HasMaxLength(20);
        builder.HasQueryFilter(e => !e.IsDeleted);
    }
}

public class GameNpcConfiguration : IEntityTypeConfiguration<GameNpc>
{
    public void Configure(EntityTypeBuilder<GameNpc> builder)
    {
        builder.HasKey(e => new { e.GameId, e.NpcId });
        builder.HasOne(e => e.Game).WithMany(g => g.GameNpcs).HasForeignKey(e => e.GameId);
        builder.HasOne(e => e.Npc).WithMany(n => n.GameNpcs).HasForeignKey(e => e.NpcId);
        builder.HasIndex(e => e.GameId);
        builder.HasIndex(e => e.PlayedByPersonId);
        builder.HasIndex(e => e.PlayedByEmail);
        builder.Property(e => e.PlayedByName).HasMaxLength(200);
        builder.Property(e => e.PlayedByEmail).HasMaxLength(200);
    }
}

public class OrganizerRoleAssignmentConfiguration : IEntityTypeConfiguration<OrganizerRoleAssignment>
{
    public void Configure(EntityTypeBuilder<OrganizerRoleAssignment> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.PersonName).IsRequired().HasMaxLength(200);
        builder.Property(e => e.PersonEmail).HasMaxLength(200);
        builder.Property(e => e.Notes).HasMaxLength(1000);

        builder.HasIndex(e => new { e.GameId, e.GameTimeSlotId, e.NpcId }).IsUnique();
        builder.HasIndex(e => new { e.GameId, e.PersonId });

        builder.HasOne(e => e.Game)
            .WithMany(g => g.OrganizerRoleAssignments)
            .HasForeignKey(e => e.GameId);
        builder.HasOne(e => e.TimeSlot)
            .WithMany(ts => ts.OrganizerRoleAssignments)
            .HasForeignKey(e => e.GameTimeSlotId);
        builder.HasOne(e => e.Npc)
            .WithMany(n => n.OrganizerRoleAssignments)
            .HasForeignKey(e => e.NpcId);
    }
}
