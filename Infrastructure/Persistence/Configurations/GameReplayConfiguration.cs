using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GamesDatabase.Api.Domain.Entities;

namespace GamesDatabase.Api.Infrastructure.Persistence.Configurations;

public class GameReplayConfiguration : IEntityTypeConfiguration<GameReplay>
{
    public void Configure(EntityTypeBuilder<GameReplay> entity)
    {
        entity.ToTable("game_replay");

        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.GameId).HasColumnName("game_id").IsRequired();
        entity.Property(e => e.ReplayTypeId).HasColumnName("replay_type_id").IsRequired();
        entity.Property(e => e.Started).HasColumnName("started");
        entity.Property(e => e.Finished).HasColumnName("finished");
        entity.Property(e => e.Grade).HasColumnName("grade");
        entity.Property(e => e.Notes).HasColumnName("notes");
        entity.Property(e => e.Released).HasColumnName("released");
        entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
        entity.Property(e => e.CreatedAt).HasColumnName("created_at");
        entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

        entity.HasOne(e => e.Game)
            .WithMany(g => g.GameReplays)
            .HasForeignKey(e => e.GameId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.ReplayType)
            .WithMany(rt => rt.Replays)
            .HasForeignKey(e => e.ReplayTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(e => e.User)
            .WithMany(u => u.Replays)
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasIndex(e => e.GameId);
        entity.HasIndex(e => e.UserId);
    }
}
