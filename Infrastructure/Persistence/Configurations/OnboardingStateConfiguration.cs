using GamesDatabase.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GamesDatabase.Api.Infrastructure.Persistence.Configurations;

public sealed class OnboardingStateConfiguration : IEntityTypeConfiguration<OnboardingState>
{
    public void Configure(EntityTypeBuilder<OnboardingState> entity)
    {
        entity.ToTable("onboarding_state");
        entity.HasKey(state => state.UserId);
        entity.Property(state => state.UserId).HasColumnName("user_id").ValueGeneratedNever();
        entity.Property(state => state.Revision).HasColumnName("revision").IsConcurrencyToken();
        entity.Property(state => state.Step).HasColumnName("step").HasMaxLength(32).IsRequired();
        entity.Property(state => state.CompletedVersion).HasColumnName("completed_version").HasMaxLength(20);
        entity.Property(state => state.SampleBatchId).HasColumnName("sample_batch_id").HasMaxLength(36);
        entity.Property(state => state.SampleStatusId).HasColumnName("sample_status_id");
        entity.Property(state => state.UpdatedAt).HasColumnName("updated_at");
        entity.HasOne(state => state.User).WithOne().HasForeignKey<OnboardingState>(state => state.UserId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne<GameStatus>().WithMany().HasForeignKey(state => state.SampleStatusId).OnDelete(DeleteBehavior.SetNull);
    }
}
