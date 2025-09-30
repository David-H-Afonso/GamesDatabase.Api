using Microsoft.EntityFrameworkCore;
using GamesDatabase.Api.Models;
using static GamesDatabase.Api.Models.SpecialStatusType;

namespace GamesDatabase.Api.Data;

public class GamesDbContext : DbContext
{
    public GamesDbContext(DbContextOptions<GamesDbContext> options) : base(options)
    {
        // Desactivar lazy loading para evitar ciclos
        ChangeTracker.LazyLoadingEnabled = false;
    }

    public override int SaveChanges()
    {
        UpdateTimestamps();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UpdateTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void UpdateTimestamps()
    {
        var entries = ChangeTracker.Entries<Game>();

        foreach (var entry in entries)
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = DateTime.UtcNow;
                entry.Entity.UpdatedAt = DateTime.UtcNow;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = DateTime.UtcNow;
                // Prevent updating CreatedAt
                entry.Property(e => e.CreatedAt).IsModified = false;
            }
        }
    }

    public DbSet<Game> Games { get; set; }
    public DbSet<GamePlatform> GamePlatforms { get; set; }
    public DbSet<GamePlayWith> GamePlayWiths { get; set; }
    public DbSet<GamePlayedStatus> GamePlayedStatuses { get; set; }
    public DbSet<GameStatus> GameStatuses { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure table names to match SQLite schema
        modelBuilder.Entity<Game>().ToTable("game");
        modelBuilder.Entity<GamePlatform>().ToTable("game_platform");
        modelBuilder.Entity<GamePlayWith>().ToTable("game_play_with");
        modelBuilder.Entity<GamePlayedStatus>().ToTable("game_played_status");
        modelBuilder.Entity<GameStatus>().ToTable("game_status");

        // Configure Game entity
        modelBuilder.Entity<Game>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.StatusId).HasColumnName("status_id").IsRequired();
            entity.Property(e => e.Name).HasColumnName("name").IsRequired();
            entity.Property(e => e.Grade).HasColumnName("grade");
            entity.Property(e => e.Critic).HasColumnName("critic");
            entity.Property(e => e.Story).HasColumnName("story");
            entity.Property(e => e.Completion).HasColumnName("completion");
            entity.Property(e => e.Score).HasColumnName("score");
            entity.Property(e => e.PlatformId).HasColumnName("platform_id");
            entity.Property(e => e.Released).HasColumnName("released");
            entity.Property(e => e.Started).HasColumnName("started");
            entity.Property(e => e.Finished).HasColumnName("finished");
            entity.Property(e => e.Comment).HasColumnName("comment");
            entity.Property(e => e.PlayWithId).HasColumnName("play_with_id");
            entity.Property(e => e.PlayedStatusId).HasColumnName("played_status_id");
            entity.Property(e => e.Logo).HasColumnName("logo");
            entity.Property(e => e.Cover).HasColumnName("cover");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

            // Configure relationships
            entity.HasOne(e => e.Status)
                .WithMany(s => s.Games)
                .HasForeignKey(e => e.StatusId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Platform)
                .WithMany(p => p.Games)
                .HasForeignKey(e => e.PlatformId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.PlayWith)
                .WithMany(pw => pw.Games)
                .HasForeignKey(e => e.PlayWithId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.PlayedStatus)
                .WithMany(ps => ps.Games)
                .HasForeignKey(e => e.PlayedStatusId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Configure GamePlatform entity
        modelBuilder.Entity<GamePlatform>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name").IsRequired();
            entity.Property(e => e.SortOrder).HasColumnName("sort_order").HasDefaultValue(0);
            entity.Property(e => e.IsActive).HasColumnName("is_active").HasDefaultValue(true);
            entity.Property(e => e.Color).HasColumnName("color").HasDefaultValue("#ffffff");

            entity.HasIndex(e => e.Name).IsUnique();
        });

        // Configure GamePlayWith entity
        modelBuilder.Entity<GamePlayWith>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name").IsRequired();
            entity.Property(e => e.SortOrder).HasColumnName("sort_order").HasDefaultValue(0);
            entity.Property(e => e.IsActive).HasColumnName("is_active").HasDefaultValue(true);
            entity.Property(e => e.Color).HasColumnName("color").HasDefaultValue("#ffffff");

            entity.HasIndex(e => e.Name).IsUnique();
        });

        // Configure GamePlayedStatus entity
        modelBuilder.Entity<GamePlayedStatus>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name").IsRequired();
            entity.Property(e => e.SortOrder).HasColumnName("sort_order").HasDefaultValue(0);
            entity.Property(e => e.IsActive).HasColumnName("is_active").HasDefaultValue(true);
            entity.Property(e => e.Color).HasColumnName("color").HasDefaultValue("#ffffff");

            entity.HasIndex(e => e.Name).IsUnique();
        });

        // Configure GameStatus entity
        modelBuilder.Entity<GameStatus>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name").IsRequired();
            entity.Property(e => e.SortOrder).HasColumnName("sort_order").HasDefaultValue(0);
            entity.Property(e => e.IsActive).HasColumnName("is_active").HasDefaultValue(true);
            entity.Property(e => e.Color).HasColumnName("color").HasDefaultValue("#ffffff");
            entity.Property(e => e.IsDefault).HasColumnName("is_default").HasDefaultValue(false);
            entity.Property(e => e.StatusType)
                .HasColumnName("status_type")
                .HasConversion<int>();

            entity.HasIndex(e => e.Name).IsUnique();

            // Ensure only one default status per status type
            entity.HasIndex(e => new { e.StatusType, e.IsDefault })
                .HasFilter("is_default = 1")
                .IsUnique();
        });
    }
}