using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GamesDatabase.Api.Domain.Entities;

namespace GamesDatabase.Api.Infrastructure.Persistence.Configurations;

public class DuplicateGameDismissalConfiguration : IEntityTypeConfiguration<DuplicateGameDismissal>
{
    public void Configure(EntityTypeBuilder<DuplicateGameDismissal> entity)
    {
        entity.ToTable("duplicate_game_dismissal");

        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
        entity.Property(e => e.GameIdA).HasColumnName("game_id_a").IsRequired();
        entity.Property(e => e.GameIdB).HasColumnName("game_id_b").IsRequired();
        entity.Property(e => e.CreatedAt).HasColumnName("created_at");

        entity.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.GameA)
            .WithMany()
            .HasForeignKey(e => e.GameIdA)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.GameB)
            .WithMany()
            .HasForeignKey(e => e.GameIdB)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasIndex(e => e.GameIdA);
        entity.HasIndex(e => e.GameIdB);
        entity.HasIndex(e => new { e.UserId, e.GameIdA, e.GameIdB }).IsUnique();
    }
}
