using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GamesDatabase.Api.Domain.Entities;

namespace GamesDatabase.Api.Infrastructure.Persistence.Configurations;

public class GameReplayTypeConfiguration : IEntityTypeConfiguration<GameReplayType>
{
    public void Configure(EntityTypeBuilder<GameReplayType> entity)
    {
        entity.ToTable("game_replay_type");

        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.Name).HasColumnName("name").IsRequired();
        entity.Property(e => e.SortOrder).HasColumnName("sort_order").HasDefaultValue(0);
        entity.Property(e => e.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        entity.Property(e => e.Color).HasColumnName("color").HasDefaultValue("#ffffff");
        entity.Property(e => e.IsDefault).HasColumnName("is_default").HasDefaultValue(false);
        entity.Property(e => e.ReplayType)
            .HasColumnName("replay_type")
            .HasConversion<int>();
        entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();

        entity.HasOne(e => e.User)
            .WithMany(u => u.ReplayTypes)
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasIndex(e => new { e.UserId, e.Name }).IsUnique();

        entity.HasIndex(e => new { e.UserId, e.ReplayType, e.IsDefault })
            .HasFilter("is_default = 1")
            .IsUnique();
    }
}
