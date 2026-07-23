using GamesDatabase.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GamesDatabase.Api.Infrastructure.Persistence.Configurations;

public sealed class HouseholdRefreshTokenConfiguration : IEntityTypeConfiguration<HouseholdRefreshToken>
{
    public void Configure(EntityTypeBuilder<HouseholdRefreshToken> entity)
    {
        entity.ToTable("household_refresh_token");
        entity.HasKey(item => item.Id);
        entity.Property(item => item.Id).HasColumnName("id");
        entity.Property(item => item.ConnectionId).HasColumnName("connection_id").IsRequired();
        entity.Property(item => item.FamilyId).HasColumnName("family_id").IsRequired();
        entity.Property(item => item.TokenHash).HasColumnName("token_hash").HasMaxLength(64).IsRequired();
        entity.Property(item => item.CreatedAt).HasColumnName("created_at").IsRequired();
        entity.Property(item => item.ExpiresAt).HasColumnName("expires_at").IsRequired();
        entity.Property(item => item.RevokedAt).HasColumnName("revoked_at");
        entity.Property(item => item.ReplacedByTokenId).HasColumnName("replaced_by_token_id");

        entity.HasOne(item => item.Connection)
            .WithMany(connection => connection.RefreshTokens)
            .HasForeignKey(item => item.ConnectionId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(item => item.ReplacedByToken)
            .WithMany()
            .HasForeignKey(item => item.ReplacedByTokenId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasIndex(item => item.TokenHash).IsUnique();
        entity.HasIndex(item => new { item.ConnectionId, item.FamilyId });
        entity.HasIndex(item => item.ExpiresAt);
    }
}
