using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GamesDatabase.Api.Domain.Entities;

namespace GamesDatabase.Api.Infrastructure.Persistence.Configurations;

public class GameViewConfiguration : IEntityTypeConfiguration<GameView>
{
    public void Configure(EntityTypeBuilder<GameView> entity)
    {
        entity.ToTable("game_view");

        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.Name).HasColumnName("name").IsRequired().HasMaxLength(100);
        entity.Property(e => e.Description).HasColumnName("description").HasMaxLength(500);
        entity.Property(e => e.FiltersJson).HasColumnName("filters_json").IsRequired();
        entity.Property(e => e.SortingJson).HasColumnName("sorting_json");
        entity.Property(e => e.IsPublic).HasColumnName("is_public").HasDefaultValue(true);
        entity.Property(e => e.CreatedBy).HasColumnName("created_by").HasMaxLength(50);
        entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
        entity.Property(e => e.CreatedAt).HasColumnName("created_at");
        entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        entity.Property(e => e.ModifiedSinceExport).HasColumnName("modified_since_export").HasDefaultValue(true);

        entity.HasOne(e => e.User)
            .WithMany(u => u.Views)
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        entity.HasIndex(e => new { e.UserId, e.Name }).IsUnique();
    }
}
