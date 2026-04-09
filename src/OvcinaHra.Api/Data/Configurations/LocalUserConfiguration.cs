using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OvcinaHra.Shared.Domain.Entities;

namespace OvcinaHra.Api.Data.Configurations;

public class LocalUserConfiguration : IEntityTypeConfiguration<LocalUser>
{
    public void Configure(EntityTypeBuilder<LocalUser> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.RegistraceUserId).IsRequired().HasMaxLength(100);
        builder.HasIndex(e => e.RegistraceUserId).IsUnique();
    }
}
