using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OvcinaHra.Shared.Domain.Entities;

namespace OvcinaHra.Api.Data.Configurations;

public class PersonalQuestSkillRewardConfiguration : IEntityTypeConfiguration<PersonalQuestSkillReward>
{
    public void Configure(EntityTypeBuilder<PersonalQuestSkillReward> b)
    {
        b.HasKey(e => new { e.PersonalQuestId, e.SkillId });
        b.HasOne(e => e.PersonalQuest).WithMany(q => q.SkillRewards)
            .HasForeignKey(e => e.PersonalQuestId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(e => e.Skill).WithMany()
            .HasForeignKey(e => e.SkillId).OnDelete(DeleteBehavior.Restrict);
    }
}
