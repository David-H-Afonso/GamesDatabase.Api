using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GamesDatabase.Api.Domain.Entities;

namespace GamesDatabase.Api.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> entity)
    {
        entity.ToTable("user");

        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.Username).HasColumnName("username").IsRequired().HasMaxLength(50);
        entity.Property(e => e.PasswordHash).HasColumnName("password_hash");
        entity.Property(e => e.Role).HasColumnName("role").HasConversion<int>();
        entity.Property(e => e.IsDefault).HasColumnName("is_default").HasDefaultValue(false);
        entity.Property(e => e.UseScoreColors).HasColumnName("use_score_colors");
        entity.Property(e => e.ScoreProvider).HasColumnName("score_provider");
        entity.Property(e => e.ShowPriceComparisonIcon).HasColumnName("show_price_comparison_icon");
        entity.Property(e => e.SteamId).HasColumnName("steam_id");
        entity.Property(e => e.SteamNickname).HasColumnName("steam_nickname");
        entity.Property(e => e.SteamAvatarUrl).HasColumnName("steam_avatar_url");
        entity.Property(e => e.SteamLinkedAt).HasColumnName("steam_linked_at");
        entity.Property(e => e.CreatedAt).HasColumnName("created_at");
        entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

        entity.HasIndex(e => e.Username).IsUnique();
    }
}
