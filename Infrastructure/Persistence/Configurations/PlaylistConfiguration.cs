using GamesDatabase.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GamesDatabase.Api.Infrastructure.Persistence.Configurations;

public sealed class PlaylistConfiguration : IEntityTypeConfiguration<Playlist>
{
    public void Configure(EntityTypeBuilder<Playlist> entity)
    {
        entity.ToTable("playlist");
        entity.HasKey(item => item.Id);
        entity.Property(item => item.Id).HasColumnName("id");
        entity.Property(item => item.Name).HasColumnName("name").IsRequired().HasMaxLength(150);
        entity.Property(item => item.Description).HasColumnName("description").HasMaxLength(1000);
        entity.Property(item => item.HeroUrl).HasColumnName("hero_url").HasMaxLength(2000);
        entity.Property(item => item.CoverUrl).HasColumnName("cover_url").HasMaxLength(2000);
        entity.Property(item => item.LogoUrl).HasColumnName("logo_url").HasMaxLength(2000);
        entity.Property(item => item.IsAutomatic).HasColumnName("is_automatic").HasDefaultValue(false);
        entity.Property(item => item.RulesJson).HasColumnName("rules_json");
        entity.Property(item => item.SortOrder).HasColumnName("sort_order");
        entity.Property(item => item.UserId).HasColumnName("user_id").IsRequired();
        entity.Property(item => item.CreatedAt).HasColumnName("created_at");
        entity.Property(item => item.UpdatedAt).HasColumnName("updated_at");
        entity.Property(item => item.ModifiedSinceExport).HasColumnName("modified_since_export").HasDefaultValue(true);

        entity.HasOne(item => item.User)
            .WithMany(user => user.Playlists)
            .HasForeignKey(item => item.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        entity.HasIndex(item => new { item.UserId, item.Name }).IsUnique();
        entity.HasIndex(item => new { item.UserId, item.SortOrder });
    }
}
