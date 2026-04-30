using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OvcinaHra.Shared.Domain.Entities;

namespace OvcinaHra.Api.Data.Configurations;

public class WorldChangeConfiguration : IEntityTypeConfiguration<WorldChange>
{
    public void Configure(EntityTypeBuilder<WorldChange> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.EntityType).IsRequired().HasMaxLength(100);
        builder.Property(e => e.EntityName).IsRequired().HasMaxLength(300);
        builder.Property(e => e.Operation).HasConversion<string>().HasMaxLength(32);
        builder.Property(e => e.ActorUserId).IsRequired().HasMaxLength(200);
        builder.Property(e => e.ActorDisplayName).IsRequired().HasMaxLength(200);

        builder.HasIndex(e => e.ChangedAtUtc);
        builder.HasIndex(e => new { e.GameId, e.ChangedAtUtc });
        builder.HasIndex(e => new { e.EntityType, e.EntityId });
    }
}
