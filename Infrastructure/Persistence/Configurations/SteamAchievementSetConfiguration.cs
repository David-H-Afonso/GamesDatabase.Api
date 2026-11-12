using GamesDatabase.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GamesDatabase.Api.Infrastructure.Persistence.Configurations;

public class SteamAchievementSetConfiguration : IEntityTypeConfiguration<SteamAchievementSet>
{
    public void Configure(EntityTypeBuilder<SteamAchievementSet> entity)
    {
        entity.ToTable("steam_achievement_set");
        entity.HasKey(set => set.Id);
        entity.Property(set => set.Id).HasColumnName("id");
        entity.Property(set => set.UserId).HasColumnName("user_id");
        entity.Property(set => set.GameId).HasColumnName("game_id");
        entity.Property(set => set.SteamAppId).HasColumnName("steam_app_id");
        entity.Property(set => set.GroupId).HasColumnName("group_id");
        entity.Property(set => set.DisplayName).HasColumnName("display_name").HasMaxLength(500);
        entity.Property(set => set.DlcAppId).HasColumnName("dlc_app_id");
        entity.Property(set => set.SortOrder).HasColumnName("sort_order");
        entity.Property(set => set.Archived).HasColumnName("archived");
        entity.Property(set => set.DeveloperOnly).HasColumnName("developer_only");
        entity.Property(set => set.IsPublic).HasColumnName("is_public").HasDefaultValue(true);
        entity.Property(set => set.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        entity.Property(set => set.TotalAchievements).HasColumnName("total_achievements");
        entity.Property(set => set.CompletionAchievements).HasColumnName("completion_achievements");
        entity.Property(set => set.IsAchievable).HasColumnName("is_achievable");
        entity.Property(set => set.IsCompleted).HasColumnName("is_completed");
        entity.Property(set => set.CompletedAt).HasColumnName("completed_at");
        entity.Property(set => set.LastSynced).HasColumnName("last_synced");
        entity.HasOne(set => set.User).WithMany().HasForeignKey(set => set.UserId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne(set => set.Game).WithMany().HasForeignKey(set => set.GameId).OnDelete(DeleteBehavior.SetNull);
        entity.HasIndex(set => new { set.UserId, set.SteamAppId, set.GroupId }).IsUnique();
        entity.HasIndex(set => set.GameId);
    }
}
