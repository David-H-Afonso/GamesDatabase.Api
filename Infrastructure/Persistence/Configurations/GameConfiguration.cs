using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GamesDatabase.Api.Domain.Entities;

namespace GamesDatabase.Api.Infrastructure.Persistence.Configurations;

public class GameConfiguration : IEntityTypeConfiguration<Game>
{
    public void Configure(EntityTypeBuilder<Game> entity)
    {
        entity.ToTable("game");

        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.StatusId).HasColumnName("status_id").IsRequired();
        entity.Property(e => e.Name).HasColumnName("name").IsRequired();
        entity.Property(e => e.Grade).HasColumnName("grade");
        entity.Property(e => e.Critic).HasColumnName("critic");
        entity.Property(e => e.CriticProvider).HasColumnName("critic_provider");
        entity.Property(e => e.Story).HasColumnName("story");
        entity.Property(e => e.Completion).HasColumnName("completion");
        entity.Property(e => e.Score).HasColumnName("score");
        entity.Property(e => e.PlatformId).HasColumnName("platform_id");
        entity.Property(e => e.Released).HasColumnName("released");
        entity.Property(e => e.Started).HasColumnName("started");
        entity.Property(e => e.Finished).HasColumnName("finished");
        entity.Property(e => e.Comment).HasColumnName("comment");
        entity.Property(e => e.PlayedStatusId).HasColumnName("played_status_id");
        entity.Property(e => e.Logo).HasColumnName("logo");
        entity.Property(e => e.Hero).HasColumnName("hero");
        entity.Property(e => e.Cover).HasColumnName("cover");
        entity.Property(e => e.SteamAppId).HasColumnName("steam_app_id");
        entity.Property(e => e.SteamPlaytimeForever).HasColumnName("steam_playtime_forever");
        entity.Property(e => e.SteamPlaytime2Weeks).HasColumnName("steam_playtime_2weeks");
        entity.Property(e => e.SteamLastSynced).HasColumnName("steam_last_synced");
        entity.Property(e => e.SteamFinishedSource).HasColumnName("steam_finished_source");
        entity.Property(e => e.SteamFinishedLastValue).HasColumnName("steam_finished_last_value");
        entity.Property(e => e.SteamFinishedSyncedAt).HasColumnName("steam_finished_synced_at");
        entity.Property(e => e.SteamFinishedRejectedValue).HasColumnName("steam_finished_rejected_value");
        entity.Property(e => e.SteamStartedRejectedValue).HasColumnName("steam_started_rejected_value");
        entity.Property(e => e.ManualPlaytimeMinutes).HasColumnName("manual_playtime_minutes");
        entity.Property(e => e.IsManuallyCompleted).HasColumnName("IsManuallyCompleted");
        entity.Property(e => e.ModifiedSinceExport).HasColumnName("modified_since_export").HasDefaultValue(true);
        entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
        entity.Property(e => e.CreatedAt).HasColumnName("created_at");
        entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

        entity.HasOne(e => e.User)
            .WithMany(u => u.Games)
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.Status)
            .WithMany(s => s.Games)
            .HasForeignKey(e => e.StatusId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(e => e.Platform)
            .WithMany(p => p.Games)
            .HasForeignKey(e => e.PlatformId)
            .OnDelete(DeleteBehavior.SetNull);

        entity.HasOne(e => e.PlayedStatus)
            .WithMany(ps => ps.Games)
            .HasForeignKey(e => e.PlayedStatusId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
