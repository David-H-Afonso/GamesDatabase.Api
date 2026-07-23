using GamesDatabase.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GamesDatabase.Api.Infrastructure.Persistence.Configurations;

public sealed class HouseholdAccessTokenConfiguration : IEntityTypeConfiguration<HouseholdAccessToken>
{
    public void Configure(EntityTypeBuilder<HouseholdAccessToken> entity)
    {
        entity.ToTable("household_access_token");
        entity.HasKey(item => item.Id);
        entity.Property(item => item.Id).HasColumnName("id");
        entity.Property(item => item.ConnectionId).HasColumnName("connection_id").IsRequired();
        entity.Property(item => item.FamilyId).HasColumnName("family_id").IsRequired();
        entity.Property(item => item.TokenHash).HasColumnName("token_hash").HasMaxLength(64).IsRequired();
        entity.Property(item => item.CreatedAt).HasColumnName("created_at").IsRequired();
        entity.Property(item => item.ExpiresAt).HasColumnName("expires_at").IsRequired();
        entity.Property(item => item.RevokedAt).HasColumnName("revoked_at");

        entity.HasOne(item => item.Connection)
            .WithMany(connection => connection.AccessTokens)
            .HasForeignKey(item => item.ConnectionId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasIndex(item => item.TokenHash).IsUnique();
        entity.HasIndex(item => new { item.ConnectionId, item.FamilyId });
        entity.HasIndex(item => item.ExpiresAt);
    }
}
