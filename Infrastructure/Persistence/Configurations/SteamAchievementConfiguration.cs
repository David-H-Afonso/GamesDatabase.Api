using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GamesDatabase.Api.Domain.Entities;

namespace GamesDatabase.Api.Infrastructure.Persistence.Configurations;

public class SteamAchievementConfiguration : IEntityTypeConfiguration<SteamAchievement>
{
    public void Configure(EntityTypeBuilder<SteamAchievement> entity)
    {
        entity.ToTable("steam_achievement");

        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
        entity.Property(e => e.GameId).HasColumnName("game_id");
        entity.Property(e => e.SteamAppId).HasColumnName("steam_app_id").IsRequired();
        entity.Property(e => e.ApiName).HasColumnName("api_name").IsRequired().HasMaxLength(200);
        entity.Property(e => e.DisplayName).HasColumnName("display_name").IsRequired().HasMaxLength(500);
        entity.Property(e => e.Description).HasColumnName("description");
        entity.Property(e => e.Achieved).HasColumnName("achieved").HasDefaultValue(false);
        entity.Property(e => e.UnlockTime).HasColumnName("unlock_time");
        entity.Property(e => e.IconUrl).HasColumnName("icon_url");
        entity.Property(e => e.IconGrayUrl).HasColumnName("icon_gray_url");
        entity.Property(e => e.Hidden).HasColumnName("hidden").HasDefaultValue(false);
        entity.Property(e => e.LastSynced).HasColumnName("last_synced");

        entity.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.Game)
            .WithMany()
            .HasForeignKey(e => e.GameId)
            .OnDelete(DeleteBehavior.SetNull);

        entity.HasIndex(e => new { e.UserId, e.SteamAppId, e.ApiName }).IsUnique();
        entity.HasIndex(e => e.GameId);
    }
}
