using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GamesDatabase.Api.Domain.Entities;

namespace GamesDatabase.Api.Infrastructure.Persistence.Configurations;

public class GameStatusConfiguration : IEntityTypeConfiguration<GameStatus>
{
    public void Configure(EntityTypeBuilder<GameStatus> entity)
    {
        entity.ToTable("game_status");

        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.Name).HasColumnName("name").IsRequired();
        entity.Property(e => e.SortOrder).HasColumnName("sort_order").HasDefaultValue(0);
        entity.Property(e => e.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        entity.Property(e => e.Color).HasColumnName("color").HasDefaultValue("#ffffff");
        entity.Property(e => e.IsDefault).HasColumnName("is_default").HasDefaultValue(false);
        entity.Property(e => e.StatusType)
            .HasColumnName("status_type")
            .HasConversion<int>();
        entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();

        entity.HasOne(e => e.User)
            .WithMany(u => u.Statuses)
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasIndex(e => new { e.UserId, e.Name }).IsUnique();

        entity.HasIndex(e => new { e.UserId, e.StatusType, e.IsDefault })
            .HasFilter("is_default = 1")
            .IsUnique();
    }
}
