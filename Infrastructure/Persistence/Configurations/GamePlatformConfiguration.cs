using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GamesDatabase.Api.Domain.Entities;

namespace GamesDatabase.Api.Infrastructure.Persistence.Configurations;

public class GamePlatformConfiguration : IEntityTypeConfiguration<GamePlatform>
{
    public void Configure(EntityTypeBuilder<GamePlatform> entity)
    {
        entity.ToTable("game_platform");

        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.Name).HasColumnName("name").IsRequired();
        entity.Property(e => e.SortOrder).HasColumnName("sort_order").HasDefaultValue(0);
        entity.Property(e => e.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        entity.Property(e => e.Color).HasColumnName("color").HasDefaultValue("#ffffff");
        entity.Property(e => e.Logo).HasColumnName("logo");
        entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();

        entity.HasOne(e => e.User)
            .WithMany(u => u.Platforms)
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasIndex(e => new { e.UserId, e.Name }).IsUnique();
    }
}
