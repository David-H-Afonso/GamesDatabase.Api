using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GamesDatabase.Api.Domain.Entities;

namespace GamesDatabase.Api.Infrastructure.Persistence.Configurations;

public class GameExportCacheConfiguration : IEntityTypeConfiguration<GameExportCache>
{
    public void Configure(EntityTypeBuilder<GameExportCache> entity)
    {
        entity.ToTable("game_export_cache");

        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.GameId).HasColumnName("game_id").IsRequired();
        entity.Property(e => e.LastExportedAt).HasColumnName("last_exported_at");
        entity.Property(e => e.LogoDownloaded).HasColumnName("logo_downloaded").HasDefaultValue(false);
        entity.Property(e => e.HeroDownloaded).HasColumnName("hero_downloaded").HasDefaultValue(false);
        entity.Property(e => e.CoverDownloaded).HasColumnName("cover_downloaded").HasDefaultValue(false);
        entity.Property(e => e.LogoUrl).HasColumnName("logo_url");
        entity.Property(e => e.HeroUrl).HasColumnName("hero_url");
        entity.Property(e => e.CoverUrl).HasColumnName("cover_url");
        entity.Property(e => e.CreatedAt).HasColumnName("created_at");
        entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

        entity.HasOne(e => e.Game)
            .WithMany()
            .HasForeignKey(e => e.GameId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasIndex(e => e.GameId).IsUnique();
    }
}
