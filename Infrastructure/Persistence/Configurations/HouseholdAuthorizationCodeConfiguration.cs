using GamesDatabase.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GamesDatabase.Api.Infrastructure.Persistence.Configurations;

public sealed class HouseholdAuthorizationCodeConfiguration : IEntityTypeConfiguration<HouseholdAuthorizationCode>
{
    public void Configure(EntityTypeBuilder<HouseholdAuthorizationCode> entity)
    {
        entity.ToTable("household_authorization_code");
        entity.HasKey(item => item.Id);
        entity.Property(item => item.Id).HasColumnName("id");
        entity.Property(item => item.ConnectionId).HasColumnName("connection_id").IsRequired();
        entity.Property(item => item.CodeHash).HasColumnName("code_hash").HasMaxLength(64).IsRequired();
        entity.Property(item => item.RedirectUri).HasColumnName("redirect_uri").HasMaxLength(2048).IsRequired();
        entity.Property(item => item.CodeChallenge).HasColumnName("code_challenge").HasMaxLength(128).IsRequired();
        entity.Property(item => item.GrantedScopes).HasColumnName("granted_scopes").HasMaxLength(500).IsRequired();
        entity.Property(item => item.CreatedAt).HasColumnName("created_at").IsRequired();
        entity.Property(item => item.ExpiresAt).HasColumnName("expires_at").IsRequired();
        entity.Property(item => item.ConsumedAt).HasColumnName("consumed_at");

        entity.HasOne(item => item.Connection)
            .WithMany(connection => connection.AuthorizationCodes)
            .HasForeignKey(item => item.ConnectionId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasIndex(item => item.CodeHash).IsUnique();
        entity.HasIndex(item => item.ExpiresAt);
    }
}
