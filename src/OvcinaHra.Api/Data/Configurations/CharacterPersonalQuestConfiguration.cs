using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OvcinaHra.Shared.Domain.Entities;

namespace OvcinaHra.Api.Data.Configurations;

public class CharacterPersonalQuestConfiguration : IEntityTypeConfiguration<CharacterPersonalQuest>
{
    public void Configure(EntityTypeBuilder<CharacterPersonalQuest> b)
    {
        b.HasKey(e => e.CharacterId);        // one-to-one: a character has at most 1 PQ
        b.HasOne(e => e.Character).WithMany()
            .HasForeignKey(e => e.CharacterId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(e => e.PersonalQuest).WithMany(q => q.CharacterAssignments)
            .HasForeignKey(e => e.PersonalQuestId).OnDelete(DeleteBehavior.Restrict);
    }
}
