using GamesDatabase.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GamesDatabase.Api.Infrastructure.Persistence.Configurations;

public sealed class HouseholdConnectionConfiguration : IEntityTypeConfiguration<HouseholdConnection>
{
    public void Configure(EntityTypeBuilder<HouseholdConnection> entity)
    {
        entity.ToTable("household_connection");
        entity.HasKey(item => item.Id);
        entity.Property(item => item.Id).HasColumnName("id");
        entity.Property(item => item.UserId).HasColumnName("user_id").IsRequired();
        entity.Property(item => item.ClientId).HasColumnName("client_id").HasMaxLength(100).IsRequired();
        entity.Property(item => item.AccountId).HasColumnName("account_id").HasMaxLength(100).IsRequired();
        entity.Property(item => item.GrantedScopes).HasColumnName("granted_scopes").HasMaxLength(500).IsRequired();
        entity.Property(item => item.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
        entity.Property(item => item.CreatedAt).HasColumnName("created_at").IsRequired();
        entity.Property(item => item.UpdatedAt).HasColumnName("updated_at").IsRequired();
        entity.Property(item => item.LastUsedAt).HasColumnName("last_used_at");
        entity.Property(item => item.RevokedAt).HasColumnName("revoked_at");

        entity.HasOne(item => item.User)
            .WithMany()
            .HasForeignKey(item => item.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasIndex(item => new { item.UserId, item.ClientId }).IsUnique();
        entity.HasIndex(item => item.AccountId).IsUnique();
    }
}
