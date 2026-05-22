using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GamesDatabase.Api.Domain.Entities;

namespace GamesDatabase.Api.Infrastructure.Persistence.Configurations;

public class GamePlayWithConfiguration : IEntityTypeConfiguration<GamePlayWith>
{
    public void Configure(EntityTypeBuilder<GamePlayWith> entity)
    {
        entity.ToTable("game_play_with");

        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.Name).HasColumnName("name").IsRequired();
        entity.Property(e => e.SortOrder).HasColumnName("sort_order").HasDefaultValue(0);
        entity.Property(e => e.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        entity.Property(e => e.Color).HasColumnName("color").HasDefaultValue("#ffffff");
        entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();

        entity.HasOne(e => e.User)
            .WithMany(u => u.PlayWiths)
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasIndex(e => new { e.UserId, e.Name }).IsUnique();
    }
}
