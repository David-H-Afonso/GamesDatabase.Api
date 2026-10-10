using GamesDatabase.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GamesDatabase.Api.Infrastructure.Persistence.Configurations;

public sealed class PlaylistGroupConfiguration : IEntityTypeConfiguration<PlaylistGroup>
{
    public void Configure(EntityTypeBuilder<PlaylistGroup> entity)
    {
        entity.ToTable("playlist_group");
        entity.HasKey(group => group.Id);
        entity.Property(group => group.Id).HasColumnName("id");
        entity.Property(group => group.UserId).HasColumnName("user_id");
        entity.Property(group => group.Name).HasColumnName("name").HasMaxLength(150).IsRequired();
        entity.Property(group => group.SortOrder).HasColumnName("sort_order");
        entity.HasOne<User>().WithMany().HasForeignKey(group => group.UserId).OnDelete(DeleteBehavior.Cascade);
        entity.HasIndex(group => new { group.UserId, group.Name }).IsUnique();
    }
}
