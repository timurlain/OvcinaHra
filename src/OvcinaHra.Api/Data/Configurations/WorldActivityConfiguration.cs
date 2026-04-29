using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OvcinaHra.Shared.Domain.Entities;

namespace OvcinaHra.Api.Data.Configurations;

public class WorldActivityConfiguration : IEntityTypeConfiguration<WorldActivity>
{
    public void Configure(EntityTypeBuilder<WorldActivity> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.OrganizerUserId).IsRequired().HasMaxLength(200);
        builder.Property(a => a.OrganizerName).IsRequired().HasMaxLength(200);
        builder.Property(a => a.ActivityType).HasConversion<string>().HasMaxLength(64);
        builder.Property(a => a.Description).IsRequired().HasMaxLength(500);
        builder.Property(a => a.DataJson).HasColumnType("jsonb");

        builder.HasOne(a => a.Game)
            .WithMany(g => g.WorldActivities)
            .HasForeignKey(a => a.GameId);
        builder.HasOne(a => a.Location)
            .WithMany()
            .HasForeignKey(a => a.LocationId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(a => a.CharacterAssignment)
            .WithMany()
            .HasForeignKey(a => a.CharacterAssignmentId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(a => a.Quest)
            .WithMany()
            .HasForeignKey(a => a.QuestId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(a => new { a.GameId, a.TimestampUtc });
        builder.HasIndex(a => a.LocationId);
    }
}
