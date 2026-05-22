using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GamesDatabase.Api.Domain.Entities;

namespace GamesDatabase.Api.Infrastructure.Persistence.Configurations;

public class BackupScheduleConfiguration : IEntityTypeConfiguration<BackupSchedule>
{
    public void Configure(EntityTypeBuilder<BackupSchedule> entity)
    {
        entity.ToTable("backup_schedule");

        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
        entity.Property(e => e.IsEnabled).HasColumnName("is_enabled").HasDefaultValue(false);
        entity.Property(e => e.BackupHour).HasColumnName("backup_hour").HasDefaultValue(3);
        entity.Property(e => e.BackupMinute).HasColumnName("backup_minute").HasDefaultValue(0);
        entity.Property(e => e.BackupType).HasColumnName("backup_type").HasDefaultValue("full");
        entity.Property(e => e.DestinationPath).HasColumnName("destination_path").HasDefaultValue("/backups");
        entity.Property(e => e.FileNamePrefix).HasColumnName("file_name_prefix").HasDefaultValue("");
        entity.Property(e => e.FileNameSuffix).HasColumnName("file_name_suffix").HasDefaultValue("");
        entity.Property(e => e.RetentionCount).HasColumnName("retention_count").HasDefaultValue(7);
        entity.Property(e => e.LastRunAt).HasColumnName("last_run_at");
        entity.Property(e => e.LastRunStatus).HasColumnName("last_run_status").HasDefaultValue("never");
        entity.Property(e => e.LastRunMessage).HasColumnName("last_run_message");

        entity.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasIndex(e => e.UserId);
    }
}
