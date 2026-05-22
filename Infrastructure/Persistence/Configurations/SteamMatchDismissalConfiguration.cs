using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GamesDatabase.Api.Domain.Entities;

namespace GamesDatabase.Api.Infrastructure.Persistence.Configurations;

public class SteamMatchDismissalConfiguration : IEntityTypeConfiguration<SteamMatchDismissal>
{
    public void Configure(EntityTypeBuilder<SteamMatchDismissal> entity)
    {
        entity.ToTable("steam_match_dismissal");

        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
        entity.Property(e => e.SteamAppId).HasColumnName("steam_app_id").IsRequired();
        entity.Property(e => e.GameId).HasColumnName("game_id").IsRequired();
        entity.Property(e => e.CreatedAt).HasColumnName("created_at");

        entity.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.Game)
            .WithMany()
            .HasForeignKey(e => e.GameId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasIndex(e => e.GameId);
        entity.HasIndex(e => new { e.UserId, e.SteamAppId, e.GameId }).IsUnique();
    }
}
