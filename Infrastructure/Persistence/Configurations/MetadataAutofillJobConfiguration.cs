using GamesDatabase.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GamesDatabase.Api.Infrastructure.Persistence.Configurations;

public sealed class MetadataAutofillJobConfiguration : IEntityTypeConfiguration<MetadataAutofillJob>
{
    public void Configure(EntityTypeBuilder<MetadataAutofillJob> entity)
    {
        entity.ToTable("metadata_autofill_job", table => table.HasCheckConstraint("CK_metadata_job_active_owner", "active_user_id IS NULL OR active_user_id = user_id"));
        entity.HasKey(job => job.Id);
        entity.Property(job => job.Id).HasColumnName("id");
        entity.Property(job => job.UserId).HasColumnName("user_id");
        entity.Property(job => job.ClientRequestId).HasColumnName("client_request_id");
        entity.Property(job => job.RequestHash).HasColumnName("request_hash").HasMaxLength(64).IsRequired();
        entity.Property(job => job.ActiveUserId).HasColumnName("active_user_id");
        entity.Property(job => job.Overwrite).HasColumnName("overwrite");
        entity.Property(job => job.AcceptListedPlatformCritic).HasColumnName("accept_listed_platform_critic");
        entity.Property(job => job.State).HasColumnName("state").HasMaxLength(24).IsRequired();
        entity.Property(job => job.ItemsJson).HasColumnName("items_json").IsRequired();
        entity.Property(job => job.CurrentPhase).HasColumnName("current_phase").HasMaxLength(24);
        entity.Property(job => job.CurrentGameId).HasColumnName("current_game_id");
        entity.Property(job => job.CancelRequested).HasColumnName("cancel_requested");
        entity.Property(job => job.Dismissed).HasColumnName("dismissed");
        entity.Property(job => job.Revision).HasColumnName("revision").IsConcurrencyToken();
        entity.Property(job => job.CreatedAt).HasColumnName("created_at");
        entity.Property(job => job.UpdatedAt).HasColumnName("updated_at");
        entity.Property(job => job.FinishedAt).HasColumnName("finished_at");
        entity.Property(job => job.NextAttemptAt).HasColumnName("next_attempt_at");
        entity.Property(job => job.LeaseOwner).HasColumnName("lease_owner").HasMaxLength(64);
        entity.Property(job => job.LeaseUntil).HasColumnName("lease_until");
        entity.HasIndex(job => new { job.UserId, job.ClientRequestId }).IsUnique();
        entity.HasIndex(job => job.ActiveUserId).IsUnique();
        entity.HasIndex(job => new { job.ActiveUserId, job.NextAttemptAt });
        entity.HasOne(job => job.User).WithMany().HasForeignKey(job => job.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
