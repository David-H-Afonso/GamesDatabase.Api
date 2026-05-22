using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GamesDatabase.Api.Domain.Entities;

namespace GamesDatabase.Api.Infrastructure.Persistence.Configurations;

public class GameViewExportCacheConfiguration : IEntityTypeConfiguration<GameViewExportCache>
{
    public void Configure(EntityTypeBuilder<GameViewExportCache> entity)
    {
        entity.ToTable("game_view_export_cache");

        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.GameViewId).HasColumnName("game_view_id").IsRequired();
        entity.Property(e => e.LastExportedAt).HasColumnName("last_exported_at").IsRequired();
        entity.Property(e => e.ConfigurationHash).HasColumnName("configuration_hash").HasMaxLength(64).IsRequired();
        entity.Property(e => e.CreatedAt).HasColumnName("created_at");
        entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

        entity.HasOne(e => e.GameView)
            .WithMany()
            .HasForeignKey(e => e.GameViewId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasIndex(e => e.GameViewId).IsUnique();
    }
}
