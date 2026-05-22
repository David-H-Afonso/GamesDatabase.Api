using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GamesDatabase.Api.Domain.Entities;

namespace GamesDatabase.Api.Infrastructure.Persistence.Configurations;

public class GamePlayWithMappingConfiguration : IEntityTypeConfiguration<GamePlayWithMapping>
{
    public void Configure(EntityTypeBuilder<GamePlayWithMapping> entity)
    {
        entity.ToTable("game_play_with_mapping");

        entity.HasKey(e => new { e.GameId, e.PlayWithId });
        entity.Property(e => e.GameId).HasColumnName("game_id");
        entity.Property(e => e.PlayWithId).HasColumnName("play_with_id");

        entity.HasOne(e => e.Game)
            .WithMany(g => g.GamePlayWiths)
            .HasForeignKey(e => e.GameId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.PlayWith)
            .WithMany(pw => pw.GamePlayWiths)
            .HasForeignKey(e => e.PlayWithId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
