using GamesDatabase.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GamesDatabase.Api.Infrastructure.Persistence.Configurations;

public sealed class CollectionPreferencesConfiguration : IEntityTypeConfiguration<CollectionPreferences>
{
    public void Configure(EntityTypeBuilder<CollectionPreferences> entity)
    {
        entity.ToTable("collection_preferences");
        entity.HasKey(value => value.UserId);
        entity.Property(value => value.UserId).HasColumnName("user_id").ValueGeneratedNever();
        entity.Property(value => value.HomeConfigJson).HasColumnName("home_config_json").IsRequired();
        entity.Property(value => value.GotySelectionsJson).HasColumnName("goty_selections_json").IsRequired();
        entity.Property(value => value.Revision).HasColumnName("revision").IsConcurrencyToken();
        entity.Property(value => value.UpdatedAt).HasColumnName("updated_at");
        entity.HasOne(value => value.User).WithOne().HasForeignKey<CollectionPreferences>(value => value.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
