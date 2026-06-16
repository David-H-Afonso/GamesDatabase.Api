using Microsoft.EntityFrameworkCore;
using GamesDatabase.Api.Domain.Entities;

namespace GamesDatabase.Api.Infrastructure.Persistence;

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
        var entities = ChangeTracker.Entries()
            .Where(e => e.Entity is Game or GameView or User or GameReplay);

        foreach (var entry in entities)
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity is Game game)
                {
                    game.CreatedAt = DateTime.UtcNow;
                    game.UpdatedAt = DateTime.UtcNow;
                }
                else if (entry.Entity is GameView view)
                {
                    view.CreatedAt = DateTime.UtcNow;
                    view.UpdatedAt = DateTime.UtcNow;
                }
                else if (entry.Entity is User user)
                {
                    user.CreatedAt = DateTime.UtcNow;
                    user.UpdatedAt = DateTime.UtcNow;
                }
                else if (entry.Entity is GameReplay replay && entry.State == EntityState.Added)
                {
                    replay.CreatedAt = DateTime.UtcNow;
                    replay.UpdatedAt = DateTime.UtcNow;
                }
            }
            else if (entry.State == EntityState.Modified)
            {
                if (entry.Entity is Game game)
                {
                    game.UpdatedAt = DateTime.UtcNow;

                    // Only mark as modified if it's not just the ModifiedSinceExport flag being updated
                    var modifiedProperties = entry.Properties
                        .Where(p => p.IsModified && p.Metadata.Name != "ModifiedSinceExport" && p.Metadata.Name != "UpdatedAt")
                        .ToList();

                    if (modifiedProperties.Any())
                    {
                        game.ModifiedSinceExport = true;
                    }

                    entry.Property("CreatedAt").IsModified = false;
                }
                else if (entry.Entity is GameView view)
                {
                    view.UpdatedAt = DateTime.UtcNow;

                    // Only mark as modified if it's not just the ModifiedSinceExport flag being updated
                    var modifiedProperties = entry.Properties
                        .Where(p => p.IsModified && p.Metadata.Name != "ModifiedSinceExport" && p.Metadata.Name != "UpdatedAt")
                        .ToList();

                    if (modifiedProperties.Any())
                    {
                        view.ModifiedSinceExport = true;
                    }

                    entry.Property("CreatedAt").IsModified = false;
                }
                else if (entry.Entity is User user)
                {
                    user.UpdatedAt = DateTime.UtcNow;
                    entry.Property("CreatedAt").IsModified = false;
                }
                else if (entry.Entity is GameReplay replayModified)
                {
                    replayModified.UpdatedAt = DateTime.UtcNow;
                    entry.Property("CreatedAt").IsModified = false;
                }
            }
        }
    }
    public DbSet<User> Users { get; set; }
    public DbSet<Game> Games { get; set; }
    public DbSet<GamePlatform> GamePlatforms { get; set; }
    public DbSet<GamePlayWith> GamePlayWiths { get; set; }
    public DbSet<GamePlayedStatus> GamePlayedStatuses { get; set; }
    public DbSet<GameStatus> GameStatuses { get; set; }
    public DbSet<GameView> GameViews { get; set; }
    public DbSet<GamePlayWithMapping> GamePlayWithMappings { get; set; }
    public DbSet<GameExportCache> GameExportCaches { get; set; }
    public DbSet<GameViewExportCache> GameViewExportCaches { get; set; }
    public DbSet<GameReplayType> GameReplayTypes { get; set; }
    public DbSet<GameReplay> GameReplays { get; set; }
    public DbSet<GameHistoryEntry> GameHistoryEntries { get; set; }
    public DbSet<BackupSchedule> BackupSchedules { get; set; }
    public DbSet<SteamAchievement> SteamAchievements { get; set; }
    public DbSet<SteamAppCache> SteamAppCaches { get; set; }
    public DbSet<SteamMatchDismissal> SteamMatchDismissals { get; set; }
    public DbSet<DuplicateGameDismissal> DuplicateGameDismissals { get; set; }
    public DbSet<RefreshToken> RefreshTokens { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(GamesDbContext).Assembly);
    }
}
