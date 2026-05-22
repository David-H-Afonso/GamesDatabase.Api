using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GamesDatabase.Api.Domain.Entities;

namespace GamesDatabase.Api.Infrastructure.Persistence.Configurations;

public class GameHistoryEntryConfiguration : IEntityTypeConfiguration<GameHistoryEntry>
{
    public void Configure(EntityTypeBuilder<GameHistoryEntry> entity)
    {
        entity.ToTable("game_history_entry");

        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.GameId).HasColumnName("game_id"); // nullable
        entity.Property(e => e.GameName).HasColumnName("game_name").IsRequired();
        entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
        entity.Property(e => e.Field).HasColumnName("field").IsRequired();
        entity.Property(e => e.OldValue).HasColumnName("old_value");
        entity.Property(e => e.NewValue).HasColumnName("new_value");
        entity.Property(e => e.Description).HasColumnName("description").IsRequired();
        entity.Property(e => e.ActionType).HasColumnName("action_type").IsRequired();
        entity.Property(e => e.ChangedAt).HasColumnName("changed_at");

        // ON DELETE SET NULL — historial persiste aunque el juego se borre
        entity.HasOne(e => e.Game)
            .WithMany()
            .HasForeignKey(e => e.GameId)
            .OnDelete(DeleteBehavior.SetNull);

        entity.HasOne(e => e.User)
            .WithMany(u => u.HistoryEntries)
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasIndex(e => e.GameId);
        entity.HasIndex(e => e.UserId);
        entity.HasIndex(e => e.ChangedAt);
    }
}
