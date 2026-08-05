using GamesDatabase.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GamesDatabase.Api.Infrastructure.Persistence.Configurations;

public sealed class PlaylistItemConfiguration : IEntityTypeConfiguration<PlaylistItem>
{
    public void Configure(EntityTypeBuilder<PlaylistItem> entity)
    {
        entity.ToTable("playlist_item");
        entity.HasKey(item => item.Id);
        entity.Property(item => item.Id).HasColumnName("id");
        entity.Property(item => item.PlaylistId).HasColumnName("playlist_id").IsRequired();
        entity.Property(item => item.GameId).HasColumnName("game_id").IsRequired();
        entity.Property(item => item.Position).HasColumnName("position").IsRequired();

        entity.HasOne(item => item.Playlist)
            .WithMany(playlist => playlist.Items)
            .HasForeignKey(item => item.PlaylistId)
            .OnDelete(DeleteBehavior.Cascade);
        entity.HasOne(item => item.Game)
            .WithMany()
            .HasForeignKey(item => item.GameId)
            .OnDelete(DeleteBehavior.Cascade);
        entity.HasIndex(item => new { item.PlaylistId, item.GameId }).IsUnique();
        entity.HasIndex(item => new { item.PlaylistId, item.Position });
    }
}
