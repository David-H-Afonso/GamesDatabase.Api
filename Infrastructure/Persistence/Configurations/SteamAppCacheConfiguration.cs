using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GamesDatabase.Api.Domain.Entities;

namespace GamesDatabase.Api.Infrastructure.Persistence.Configurations;

public class SteamAppCacheConfiguration : IEntityTypeConfiguration<SteamAppCache>
{
    public void Configure(EntityTypeBuilder<SteamAppCache> entity)
    {
        entity.ToTable("steam_app_cache");

        entity.HasKey(e => e.AppId);
        entity.Property(e => e.AppId).HasColumnName("app_id");
        entity.Property(e => e.Name).HasColumnName("name").IsRequired();
        entity.Property(e => e.Developer).HasColumnName("developer");
        entity.Property(e => e.Publisher).HasColumnName("publisher");
        entity.Property(e => e.GenresJson).HasColumnName("genres_json");
        entity.Property(e => e.CategoriesJson).HasColumnName("categories_json");
        entity.Property(e => e.ReleaseDate).HasColumnName("release_date");
        entity.Property(e => e.MetacriticScore).HasColumnName("metacritic_score");
        entity.Property(e => e.HeaderImageUrl).HasColumnName("header_image_url");
        entity.Property(e => e.BackgroundImageUrl).HasColumnName("background_image_url");
        entity.Property(e => e.Price).HasColumnName("price");
        entity.Property(e => e.IsFree).HasColumnName("is_free").HasDefaultValue(false);
        entity.Property(e => e.LastFetched).HasColumnName("last_fetched");
    }
}
