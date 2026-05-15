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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>().ToTable("user");
        modelBuilder.Entity<Game>().ToTable("game");
        modelBuilder.Entity<GamePlatform>().ToTable("game_platform");
        modelBuilder.Entity<GamePlayWith>().ToTable("game_play_with");
        modelBuilder.Entity<GamePlayedStatus>().ToTable("game_played_status");
        modelBuilder.Entity<GameStatus>().ToTable("game_status");
        modelBuilder.Entity<GameView>().ToTable("game_view");
        modelBuilder.Entity<GamePlayWithMapping>().ToTable("game_play_with_mapping");
        modelBuilder.Entity<GameExportCache>().ToTable("game_export_cache");
        modelBuilder.Entity<GameReplayType>().ToTable("game_replay_type");
        modelBuilder.Entity<GameReplay>().ToTable("game_replay");
        modelBuilder.Entity<GameHistoryEntry>().ToTable("game_history_entry");
        modelBuilder.Entity<BackupSchedule>().ToTable("backup_schedule");
        modelBuilder.Entity<SteamAchievement>().ToTable("steam_achievement");
        modelBuilder.Entity<SteamAppCache>().ToTable("steam_app_cache");
        modelBuilder.Entity<SteamMatchDismissal>().ToTable("steam_match_dismissal");

        // Configure User entity
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Username).HasColumnName("username").IsRequired().HasMaxLength(50);
            entity.Property(e => e.PasswordHash).HasColumnName("password_hash");
            entity.Property(e => e.Role).HasColumnName("role").HasConversion<int>();
            entity.Property(e => e.IsDefault).HasColumnName("is_default").HasDefaultValue(false);
            entity.Property(e => e.UseScoreColors).HasColumnName("use_score_colors");
            entity.Property(e => e.ScoreProvider).HasColumnName("score_provider");
            entity.Property(e => e.ShowPriceComparisonIcon).HasColumnName("show_price_comparison_icon");
            entity.Property(e => e.SteamId).HasColumnName("steam_id");
            entity.Property(e => e.SteamNickname).HasColumnName("steam_nickname");
            entity.Property(e => e.SteamAvatarUrl).HasColumnName("steam_avatar_url");
            entity.Property(e => e.SteamLinkedAt).HasColumnName("steam_linked_at");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

            entity.HasIndex(e => e.Username).IsUnique();
        });

        // Configure Game entity
        modelBuilder.Entity<Game>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.StatusId).HasColumnName("status_id").IsRequired();
            entity.Property(e => e.Name).HasColumnName("name").IsRequired();
            entity.Property(e => e.Grade).HasColumnName("grade");
            entity.Property(e => e.Critic).HasColumnName("critic");
            entity.Property(e => e.CriticProvider).HasColumnName("critic_provider");
            entity.Property(e => e.Story).HasColumnName("story");
            entity.Property(e => e.Completion).HasColumnName("completion");
            entity.Property(e => e.Score).HasColumnName("score");
            entity.Property(e => e.PlatformId).HasColumnName("platform_id");
            entity.Property(e => e.Released).HasColumnName("released");
            entity.Property(e => e.Started).HasColumnName("started");
            entity.Property(e => e.Finished).HasColumnName("finished");
            entity.Property(e => e.Comment).HasColumnName("comment");
            entity.Property(e => e.PlayedStatusId).HasColumnName("played_status_id");
            entity.Property(e => e.Logo).HasColumnName("logo");
            entity.Property(e => e.Cover).HasColumnName("cover");
            entity.Property(e => e.SteamAppId).HasColumnName("steam_app_id");
            entity.Property(e => e.SteamPlaytimeForever).HasColumnName("steam_playtime_forever");
            entity.Property(e => e.SteamPlaytime2Weeks).HasColumnName("steam_playtime_2weeks");
            entity.Property(e => e.SteamLastSynced).HasColumnName("steam_last_synced");
            entity.Property(e => e.SteamFinishedSource).HasColumnName("steam_finished_source");
            entity.Property(e => e.SteamFinishedLastValue).HasColumnName("steam_finished_last_value");
            entity.Property(e => e.SteamFinishedSyncedAt).HasColumnName("steam_finished_synced_at");
            entity.Property(e => e.SteamFinishedRejectedValue).HasColumnName("steam_finished_rejected_value");
            entity.Property(e => e.ManualPlaytimeMinutes).HasColumnName("manual_playtime_minutes");
            entity.Property(e => e.IsManuallyCompleted).HasColumnName("IsManuallyCompleted");
            entity.Property(e => e.ModifiedSinceExport).HasColumnName("modified_since_export").HasDefaultValue(true);
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

            entity.HasOne(e => e.User)
                .WithMany(u => u.Games)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Status)
                .WithMany(s => s.Games)
                .HasForeignKey(e => e.StatusId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Platform)
                .WithMany(p => p.Games)
                .HasForeignKey(e => e.PlatformId)
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
            entity.Property(e => e.Logo).HasColumnName("logo");
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();

            entity.HasOne(e => e.User)
                .WithMany(u => u.Platforms)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.UserId, e.Name }).IsUnique();
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
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();

            entity.HasOne(e => e.User)
                .WithMany(u => u.PlayWiths)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.UserId, e.Name }).IsUnique();
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
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();

            entity.HasOne(e => e.User)
                .WithMany(u => u.PlayedStatuses)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.UserId, e.Name }).IsUnique();
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
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();

            entity.HasOne(e => e.User)
                .WithMany(u => u.Statuses)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.UserId, e.Name }).IsUnique();

            entity.HasIndex(e => new { e.UserId, e.StatusType, e.IsDefault })
                .HasFilter("is_default = 1")
                .IsUnique();
        });

        // Configure GamePlayWithMapping entity (many-to-many)
        modelBuilder.Entity<GamePlayWithMapping>(entity =>
        {
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
        });

        // Configure GameView entity
        modelBuilder.Entity<GameView>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name").IsRequired().HasMaxLength(100);
            entity.Property(e => e.Description).HasColumnName("description").HasMaxLength(500);
            entity.Property(e => e.FiltersJson).HasColumnName("filters_json").IsRequired();
            entity.Property(e => e.SortingJson).HasColumnName("sorting_json");
            entity.Property(e => e.IsPublic).HasColumnName("is_public").HasDefaultValue(true);
            entity.Property(e => e.CreatedBy).HasColumnName("created_by").HasMaxLength(50);
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.Property(e => e.ModifiedSinceExport).HasColumnName("modified_since_export").HasDefaultValue(true);

            entity.HasOne(e => e.User)
                .WithMany(u => u.Views)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.UserId, e.Name }).IsUnique();
        });

        // Configure GameViewExportCache entity
        modelBuilder.Entity<GameViewExportCache>(entity =>
        {
            entity.ToTable("game_view_export_cache");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.GameViewId).HasColumnName("game_view_id").IsRequired();
            entity.Property(e => e.LastExportedAt).HasColumnName("last_exported_at").IsRequired();
            entity.Property(e => e.ConfigurationHash).HasColumnName("configuration_hash").HasMaxLength(64).IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

            entity.HasOne(e => e.GameView)
                .WithMany()
                .HasForeignKey(e => e.GameViewId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.GameViewId).IsUnique();
        });

        // Configure GameExportCache entity
        modelBuilder.Entity<GameExportCache>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.GameId).HasColumnName("game_id").IsRequired();
            entity.Property(e => e.LastExportedAt).HasColumnName("last_exported_at");
            entity.Property(e => e.LogoDownloaded).HasColumnName("logo_downloaded").HasDefaultValue(false);
            entity.Property(e => e.CoverDownloaded).HasColumnName("cover_downloaded").HasDefaultValue(false);
            entity.Property(e => e.LogoUrl).HasColumnName("logo_url");
            entity.Property(e => e.CoverUrl).HasColumnName("cover_url");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

            entity.HasOne(e => e.Game)
                .WithMany()
                .HasForeignKey(e => e.GameId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.GameId).IsUnique();
        });

        // Configure GameReplayType entity
        modelBuilder.Entity<GameReplayType>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name").IsRequired();
            entity.Property(e => e.SortOrder).HasColumnName("sort_order").HasDefaultValue(0);
            entity.Property(e => e.IsActive).HasColumnName("is_active").HasDefaultValue(true);
            entity.Property(e => e.Color).HasColumnName("color").HasDefaultValue("#ffffff");
            entity.Property(e => e.IsDefault).HasColumnName("is_default").HasDefaultValue(false);
            entity.Property(e => e.ReplayType)
                .HasColumnName("replay_type")
                .HasConversion<int>();
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();

            entity.HasOne(e => e.User)
                .WithMany(u => u.ReplayTypes)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.UserId, e.Name }).IsUnique();

            // Garantiza que solo haya un tipo especial Replay por usuario
            entity.HasIndex(e => new { e.UserId, e.ReplayType, e.IsDefault })
                .HasFilter("is_default = 1")
                .IsUnique();
        });

        // Configure GameReplay entity
        modelBuilder.Entity<GameReplay>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.GameId).HasColumnName("game_id").IsRequired();
            entity.Property(e => e.ReplayTypeId).HasColumnName("replay_type_id").IsRequired();
            entity.Property(e => e.Started).HasColumnName("started");
            entity.Property(e => e.Finished).HasColumnName("finished");
            entity.Property(e => e.Grade).HasColumnName("grade");
            entity.Property(e => e.Notes).HasColumnName("notes");
            entity.Property(e => e.Released).HasColumnName("released");
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

            entity.HasOne(e => e.Game)
                .WithMany(g => g.GameReplays)
                .HasForeignKey(e => e.GameId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.ReplayType)
                .WithMany(rt => rt.Replays)
                .HasForeignKey(e => e.ReplayTypeId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.User)
                .WithMany(u => u.Replays)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.GameId);
            entity.HasIndex(e => e.UserId);
        });

        // Configure GameHistoryEntry entity
        modelBuilder.Entity<GameHistoryEntry>(entity =>
        {
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
        });

        // Configure BackupSchedule entity
        modelBuilder.Entity<BackupSchedule>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(e => e.IsEnabled).HasColumnName("is_enabled").HasDefaultValue(false);
            entity.Property(e => e.BackupHour).HasColumnName("backup_hour").HasDefaultValue(3);
            entity.Property(e => e.BackupMinute).HasColumnName("backup_minute").HasDefaultValue(0);
            entity.Property(e => e.BackupType).HasColumnName("backup_type").HasDefaultValue("full");
            entity.Property(e => e.DestinationPath).HasColumnName("destination_path").HasDefaultValue("/backups");
            entity.Property(e => e.FileNamePrefix).HasColumnName("file_name_prefix").HasDefaultValue("");
            entity.Property(e => e.FileNameSuffix).HasColumnName("file_name_suffix").HasDefaultValue("");
            entity.Property(e => e.RetentionCount).HasColumnName("retention_count").HasDefaultValue(7);
            entity.Property(e => e.LastRunAt).HasColumnName("last_run_at");
            entity.Property(e => e.LastRunStatus).HasColumnName("last_run_status").HasDefaultValue("never");
            entity.Property(e => e.LastRunMessage).HasColumnName("last_run_message");

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.UserId);
        });

        // Configure SteamAchievement entity
        modelBuilder.Entity<SteamAchievement>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(e => e.GameId).HasColumnName("game_id");
            entity.Property(e => e.SteamAppId).HasColumnName("steam_app_id").IsRequired();
            entity.Property(e => e.ApiName).HasColumnName("api_name").IsRequired().HasMaxLength(200);
            entity.Property(e => e.DisplayName).HasColumnName("display_name").IsRequired().HasMaxLength(500);
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Achieved).HasColumnName("achieved").HasDefaultValue(false);
            entity.Property(e => e.UnlockTime).HasColumnName("unlock_time");
            entity.Property(e => e.IconUrl).HasColumnName("icon_url");
            entity.Property(e => e.IconGrayUrl).HasColumnName("icon_gray_url");
            entity.Property(e => e.Hidden).HasColumnName("hidden").HasDefaultValue(false);
            entity.Property(e => e.LastSynced).HasColumnName("last_synced");

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Game)
                .WithMany()
                .HasForeignKey(e => e.GameId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(e => new { e.UserId, e.SteamAppId, e.ApiName }).IsUnique();
            entity.HasIndex(e => e.GameId);
        });

        // Configure SteamAppCache entity
        modelBuilder.Entity<SteamAppCache>(entity =>
        {
            entity.HasKey(e => e.AppId);
            entity.Property(e => e.AppId).HasColumnName("app_id");
            entity.Property(e => e.Name).HasColumnName("name").IsRequired();
            entity.Property(e => e.Developer).HasColumnName("developer");
            entity.Property(e => e.Publisher).HasColumnName("publisher");
            entity.Property(e => e.GenresJson).HasColumnName("genres_json");
            entity.Property(e => e.CategoriesJson).HasColumnName("categories_json");
            entity.Property(e => e.ReleaseDate).HasColumnName("release_date");
            entity.Property(e => e.MetacriticScore).HasColumnName("metacritic_score");
            entity.Property(e => e.HeaderImageUrl).HasColumnName("header_image_url");
            entity.Property(e => e.BackgroundImageUrl).HasColumnName("background_image_url");
            entity.Property(e => e.Price).HasColumnName("price");
            entity.Property(e => e.IsFree).HasColumnName("is_free").HasDefaultValue(false);
            entity.Property(e => e.LastFetched).HasColumnName("last_fetched");
        });

        // Configure SteamMatchDismissal entity
        modelBuilder.Entity<SteamMatchDismissal>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(e => e.SteamAppId).HasColumnName("steam_app_id").IsRequired();
            entity.Property(e => e.GameId).HasColumnName("game_id").IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Game)
                .WithMany()
                .HasForeignKey(e => e.GameId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.GameId);
            entity.HasIndex(e => new { e.UserId, e.SteamAppId, e.GameId }).IsUnique();
        });
    }
}
