# Backend Implementation Guide — Games Database API

This document explains how the Games Database API works and how to implement future changes. It is written for a developer who is stronger on the frontend and wants to understand the backend without reading every file.

---

## 1. Backend Overview

### What This API Does

Games Database is a personal game collection management system. It lets users:

- Track games with statuses, platforms, scores, dates, images, and comments
- Create custom filtered/sorted views of their game library
- Record replays, DLCs, and expansions per game
- Import/export data via CSV and ZIP (including images)
- Sync backups to a NAS via network path
- Schedule automatic backups
- Link their Steam account and import library, achievements, and playtime
- Manage catalog items (statuses, platforms, play-with options, played-statuses, replay types) with custom colors and sort orders
- View change history for each game

### Main Domains

| Domain        | What It Covers                                                                                   |
| ------------- | ------------------------------------------------------------------------------------------------ |
| Games         | Core game entries — CRUD, filtering, sorting, pagination, bulk updates                           |
| Catalogs      | User-customizable lookup tables — statuses, platforms, play-withs, played-statuses, replay types |
| Views         | Saved filter+sort configurations with complex filter groups                                      |
| Replays       | Per-game replay/DLC/expansion entries with types                                                 |
| History       | Automatic change tracking per game field                                                         |
| Export/Import | CSV full export, CSV import with merge logic, ZIP export with images, selective import/export    |
| Backup        | Scheduled ZIP backups with retention, manual trigger, NAS sync                                   |
| Steam         | Library import, achievement sync, playtime sync, match suggestions, store search, OpenID auth    |
| Users         | Multi-user with JWT auth, admin/standard roles, Steam linking                                    |
| Images        | On-demand image proxy with resize, WebP conversion, and disk caching                             |

### Main Consumers

- **React frontend** (SPA) — the primary consumer, uses `apiRoutes.ts` to define all endpoints
- **Desktop app** (Electron wrapper) — same API, detected via `GDB_DESKTOP` env var
- **Scheduled tasks** — `BackupScheduleService` runs as a background hosted service

---

## 2. Technology Stack

| Technology            | Version                                               | Purpose                                |
| --------------------- | ----------------------------------------------------- | -------------------------------------- |
| .NET                  | 9.0                                                   | Runtime                                |
| ASP.NET Core          | 9.0                                                   | Web framework                          |
| Entity Framework Core | 9.0                                                   | ORM                                    |
| SQLite                | via `Microsoft.EntityFrameworkCore.Sqlite`            | Database                               |
| JWT Bearer Auth       | `Microsoft.AspNetCore.Authentication.JwtBearer` 9.0.9 | Authentication                         |
| BCrypt.Net-Next       | 4.0.3                                                 | Password hashing                       |
| SixLabors.ImageSharp  | 3.1.12                                                | Image processing (resize, WebP)        |
| CsvHelper             | 33.1.0                                                | CSV import/export                      |
| HtmlAgilityPack       | 1.12.2                                                | HTML parsing (for image URL detection) |
| Swashbuckle           | 7.2.0                                                 | Swagger/OpenAPI                        |
| Docker                | Dockerfile + docker-compose                           | Container deployment                   |

---

## 3. Project Structure Explained

The project follows a clean architecture with clear separation of concerns. Controllers are thin and delegate to services. Business logic lives in the `Application/` layer.

```
GamesDatabase.Api/
  Application/
    Interfaces/     (IGameService, ICatalogService, IGameViewService, IGameImportExportService, IUserService, IAuthService, IGameHistoryService, IZipExportService, INetworkSyncService, ISteamApiService, ISteamAuthService, ISteamStoreService, ISteamSyncService)
    Services/       (GameService, CatalogService, GameViewService, GameImportExportService, UserService, AuthService, GameHistoryService, ViewFilterService, ZipExportService, NetworkSyncService, BackupScheduleService)
      Steam/        (SteamApiService, SteamAuthService, SteamStoreService, SteamSyncService)
    Mapping/        (MappingExtensions, GameViewMappingExtensions)
  Common/           (HttpContextExtensions, QueryHelpers, GameDateNormalizer, FolderNameHelper, NetworkPathHelper)
  Configuration/    (ServiceCollectionExtensions, settings classes)
  Contracts/        (GameDTOs, CatalogDTOs, GameViewDTOs, ReplayAndHistoryDTOs, SelectiveImportExportDTOs, SteamDTOs, UserDTOs)
  Controllers/      (18 controllers)
  Domain/
    Entities/       (18 entity files)
  Infrastructure/
    Persistence/
      GamesDbContext.cs
      DatabaseStartupHelper.cs
      Configurations/ (17 entity config files)
    Migrations/     (17 migrations)
  Middleware/       (ErrorHandlingMiddleware, UserContextMiddleware)
  Program.cs        (~80 lines)
```

### `Controllers/`

HTTP endpoints. Each controller handles one domain. All controllers are thin — they receive requests, call the appropriate service, and return the response.

- **`BaseApiController.cs`** — Abstract base with `[ApiController]`, `[Authorize]`. Provides `CurrentUserId`, `GetCurrentUserIdOrDefault()`, `RequireUserId()` helpers via `HttpContext.GetUserId()`.
- **`GamesController.cs`** — Core game CRUD. Delegates to `IGameService` for query building, filtering, sorting, pagination, and all mutations.
- **`DataExportController.cs`** — Full/selective import and export. Delegates to `IGameImportExportService`.
- **`SteamController.cs`** — Steam integration endpoints. Delegates to Steam services.
- **`GameStatusController.cs`, `GamePlatformsController.cs`, `GamePlayWithController.cs`, `GamePlayedStatusController.cs`, `GameReplayTypesController.cs`** — Catalog CRUD. All delegate to `ICatalogService`.
- **`GameViewsController.cs`** — Saved view CRUD. Delegates to `IGameViewService`.
- **`GameReplaysController.cs`** — Replay CRUD nested under `/api/games/{gameId}/replays`.
- **`GameHistoryController.cs`** — Browse/delete game change history.
- **`UsersController.cs`** — Auth (login), user CRUD, password change. Delegates to `IUserService`.
- **`ExportController.cs`** — ZIP export, network sync. Delegates to services.
- **`BackupScheduleController.cs`** — Scheduled backup management.
- **`ImageProxyController.cs`** — On-demand image resize and WebP conversion with disk caching.
- **`SteamAuthController.cs`** — Steam OpenID login/link flow.
- **`ConfigController.cs`** — Returns network sync config (smallest controller).

### `Application/Interfaces/`

Service interfaces for dependency injection:

- **`IGameService`** — Game CRUD, filtering, sorting, pagination, bulk updates
- **`ICatalogService`** — Generic catalog CRUD and reorder for all catalog entity types
- **`IGameViewService`** — Saved view CRUD, duplication, config validation
- **`IGameImportExportService`** — CSV full/selective import and export, folder analysis, image URL updates
- **`IUserService`** — User CRUD, role management, seed data
- **`IAuthService`** — JWT token generation, login authentication
- **`IGameHistoryService`** — Game field change tracking
- **`IZipExportService`** — ZIP archive building
- **`INetworkSyncService`** — NAS sync via UNC paths
- **`ISteamApiService`**, **`ISteamAuthService`**, **`ISteamStoreService`**, **`ISteamSyncService`** — Steam integration

### `Application/Services/`

Business logic implementations:

- **`GameService`** — All game CRUD, query building with view-filter support, diacritics-aware search, 15+ sort options, pagination, score calculation, bulk updates
- **`CatalogService`** — Generic catalog CRUD + reorder for statuses, platforms, play-withs, played-statuses, replay types
- **`GameViewService`** — View CRUD, duplication, reorder, configuration validation
- **`GameImportExportService`** — Full CSV export/import with merge logic, selective export/import, image URL detection, folder analysis
- **`UserService`** — User CRUD, role checks, centralized seed data
- **`AuthService`** — JWT token generation, password hashing with BCrypt, login authentication
- **`GameHistoryService`** — Records game field changes with diffing logic and max-entry pruning
- **`ViewFilterService`** — Applies view filter configurations to EF queries
- **`ZipExportService`** — Builds ZIP archives with CSV + game images
- **`NetworkSyncService`** — Syncs export files to NAS via UNC paths
- **`BackupScheduleService`** — Background hosted service for scheduled backups
- **`Steam/SteamApiService`** — Steam Web API client (owned games, achievements, player summary)
- **`Steam/SteamAuthService`** — Steam OpenID nonce/validation management
- **`Steam/SteamStoreService`** — Steam Store API client (app details, search)
- **`Steam/SteamSyncService`** — Syncs Steam library data to local games

### `Application/Mapping/`

- **`MappingExtensions.cs`** — Entity ↔ DTO mapping extension methods for all domain types
- **`GameViewMappingExtensions.cs`** — GameView entity ↔ DTO mapping with JSON deserialization

### `Common/`

Utility classes:

- **`HttpContextExtensions.cs`** — `HttpContext.GetUserId()` extension method
- **`QueryHelpers.cs`** — `QueryParameters`, `GameQueryParameters`, `PagedResult<T>` classes
- **`GameDateNormalizer.cs`** — Normalizes Steam date formats to ISO 8601
- **`FolderNameHelper.cs`** — Sanitizes game names for safe folder/file names
- **`NetworkPathHelper.cs`** — Windows UNC path authentication (WNet API + net use fallback)

### `Contracts/`

Request and response data transfer objects, grouped by domain:

- `GameDTOs.cs` — `GameDto`, `GameCreateDto`, `GameUpdateDto`, `BulkUpdateGameDto`
- `CatalogDTOs.cs` — DTOs for all catalog entities (platforms, statuses, play-withs, played-statuses, replay types)
- `GameViewDTOs.cs` — `GameViewDto`, `GameViewCreateDto`, `GameViewUpdateDto`, view configuration models
- `ReplayAndHistoryDTOs.cs` — `GameReplayDto`, `GameReplayCreateDto`, `GameHistoryEntryDto`
- `SteamDTOs.cs` — Steam-specific DTOs for library, achievements, match suggestions
- `UserDTOs.cs` — `LoginRequest`, `LoginResponse`, `CreateUserRequest`, `UserDto`
- `SelectiveImportExportDTOs.cs` — Models for selective import/export operations

### `Domain/Entities/`

EF Core entity classes. 18 entities representing database tables:

- **Core:** `Game`, `User`, `GameView`
- **Catalogs:** `GamePlatform`, `GameStatus`, `GamePlayWith`, `GamePlayedStatus`, `GameReplayType`
- **Join tables:** `GamePlayWithMapping`
- **Features:** `GameReplay`, `GameHistoryEntry`, `BackupSchedule`
- **Export:** `GameExportCache`, `GameViewExportCache`, `ExportRecord` (CSV model, not a DB entity)
- **Steam:** `SteamAchievement`, `SteamAppCache`, `SteamMatchDismissal`

### `Infrastructure/Persistence/`

- **`GamesDbContext.cs`** — EF Core DbContext. Contains 17 `DbSet<T>` properties. Uses `ApplyConfigurationsFromAssembly` to load all entity configurations. `UpdateTimestamps()` override for automatic `CreatedAt`/`UpdatedAt` + export tracking.
- **`DatabaseStartupHelper.cs`** — Database initialization: migrations, schema repair, seeding.
- **`Configurations/`** — 17 `IEntityTypeConfiguration<T>` files, one per entity, defining table names, column mappings, relationships, and indexes.

### `Configuration/`

Strongly-typed Options classes and DI registration:

- `ServiceCollectionExtensions` — Extension methods for registering all services, options, DbContext, JWT auth, CORS, Swagger
- `CorsSettings` — Allowed CORS origins
- `DatabaseSettings` — SQLite path, sensitive data logging
- `ExportSettings` — Export path, CSV delimiter/encoding
- `JwtSettings` — JWT secret, issuer, audience, expiration
- `DataExportOptions` — Full export URL
- `NetworkSyncOptions` — NAS sync path and credentials
- `SteamSettings` — Steam API key, URLs, cache TTL

### `Middleware/`

- **`ErrorHandlingMiddleware`** — Global exception handler. Maps SQLite constraint errors, `ArgumentException`, `UnauthorizedAccessException`, `KeyNotFoundException` to appropriate HTTP status codes with consistent JSON error responses.
- **`UserContextMiddleware`** — Extracts `UserId` from JWT claims and stores in `HttpContext.Items["UserId"]`. Fallback: reads `X-User-Id` header in development only.

### `Infrastructure/Migrations/`

17 EF Core migrations. Applied automatically at startup via `DatabaseStartupHelper`.

---

## 4. Startup Flow

`Program.cs` is ~80 lines. All registration and pipeline setup is extracted into extension methods in `Configuration/ServiceCollectionExtensions.cs`. Database initialization (migrations, schema repair, seeding) is handled by `Infrastructure/Persistence/DatabaseStartupHelper.cs`.

### 1. Builder Configuration (in ServiceCollectionExtensions)

```
Create WebApplicationBuilder
Detect desktop mode (GDB_DESKTOP env var)
Configure logging (Console + Debug)
Load .env file if exists
Override config with env vars (NetworkSync, JWT, Steam)
```

### 2. Service Registration (via ServiceCollectionExtensions)

```
AddOptionsConfiguration() — CorsSettings, DatabaseSettings, ExportSettings, JwtSettings, etc.
AddPersistence() — DbContext with SQLite connection string
AddApplicationServices() — all services (GameService, CatalogService, GameViewService,
  GameImportExportService, UserService, AuthService, GameHistoryService, ViewFilterService,
  ZipExportService, NetworkSyncService, BackupScheduleService, Steam services)
AddControllers with JSON options (IgnoreCycles, WhenWritingNull)
AddMemoryCache
Configure DataProtection (desktop mode only)
Configure named HttpClients ("TrustAllCerts", "ImageDownloader")
AddCorsConfiguration() — AllowSpecificOrigins + AllowAll for dev
AddJwtAuthentication() — JWT Bearer authentication
AddAuthorization
AddApiDocumentation() — SwaggerGen with JWT security definition
```

### 3. App Pipeline

```
UseSwagger + SwaggerUI
UseMiddleware<ErrorHandlingMiddleware>
DatabaseStartupHelper.InitializeAsync():
  - context.Database.Migrate()
  - EnsureCompatibilitySchema() — raw SQL safety net
  - SeedDefaultDataAsync() — creates Admin user + default catalogs
  - SeedMissingReplayTypesAsync()
UseCors (AllowAll in dev/desktop, AllowSpecificOrigins in production)
UseAuthentication
UseUserContext (custom middleware)
UseAuthorization
MapGet("/health") — anonymous health check
MapControllers
app.Run()
```

---

## 5. Database Flow

### Where the Database Is Configured

1. **Connection string:** Built in `Program.cs` from `DatabaseSettings.DatabasePath` (default: `../gamesdatabase.db`)
2. **DbContext registration:** `builder.Services.AddDbContext<GamesDbContext>(options => options.UseSqlite(connectionString))`
3. **Options class:** `Configuration/DatabaseSettings.cs` with `DatabasePath` and `EnableSensitiveDataLogging`

### DbContext Location

`Infrastructure/Persistence/GamesDbContext.cs`

### Important DbSets

| DbSet                  | Entity                | Table Name               | Purpose                                  |
| ---------------------- | --------------------- | ------------------------ | ---------------------------------------- |
| `Games`                | `Game`                | `game`                   | Core game entries                        |
| `Users`                | `User`                | `user`                   | User accounts                            |
| `GamePlatforms`        | `GamePlatform`        | `game_platform`          | Customizable platforms                   |
| `GameStatuses`         | `GameStatus`          | `game_status`            | Customizable statuses with special types |
| `GamePlayWiths`        | `GamePlayWith`        | `game_play_with`         | Play-with options                        |
| `GamePlayedStatuses`   | `GamePlayedStatus`    | `game_played_status`     | Completion-level statuses                |
| `GameViews`            | `GameView`            | `game_view`              | Saved filter/sort configs                |
| `GamePlayWithMappings` | `GamePlayWithMapping` | `game_play_with_mapping` | Many-to-many join                        |
| `GameReplays`          | `GameReplay`          | `game_replay`            | Replay/DLC entries                       |
| `GameReplayTypes`      | `GameReplayType`      | `game_replay_type`       | Replay categories                        |
| `GameHistoryEntries`   | `GameHistoryEntry`    | `game_history_entry`     | Change log                               |
| `BackupSchedules`      | `BackupSchedule`      | `backup_schedule`        | Scheduled backups                        |
| `SteamAchievements`    | `SteamAchievement`    | `steam_achievement`      | Cached Steam achievements                |
| `SteamAppCaches`       | `SteamAppCache`       | `steam_app_cache`        | Cached Steam store data                  |
| `SteamMatchDismissals` | `SteamMatchDismissal` | `steam_match_dismissal`  | Dismissed match suggestions              |
| `GameExportCaches`     | `GameExportCache`     | `game_export_cache`      | Export tracking per game                 |
| `GameViewExportCaches` | `GameViewExportCache` | `game_view_export_cache` | Export tracking per view                 |

### How Entities Become Tables

Entity-to-table mapping is defined in separate `IEntityTypeConfiguration<T>` classes under `Infrastructure/Persistence/Configurations/`. Each entity has its own configuration file:

```csharp
// Infrastructure/Persistence/Configurations/GameConfiguration.cs
public class GameConfiguration : IEntityTypeConfiguration<Game>
{
    public void Configure(EntityTypeBuilder<Game> builder)
    {
        builder.ToTable("game");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.Name).HasColumnName("name").IsRequired();
        // ... all properties mapped to snake_case columns
        builder.HasOne(e => e.Status)
            .WithMany(s => s.Games)
            .HasForeignKey(e => e.StatusId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
```

`GamesDbContext.OnModelCreating` uses `modelBuilder.ApplyConfigurationsFromAssembly(...)` to auto-discover all configurations.

### Key Relationships

```
User ──< Game          (Cascade: delete user → delete games)
User ──< GamePlatform  (Cascade)
User ──< GameStatus    (Cascade)
User ──< GamePlayWith  (Cascade)
User ──< GameView      (Cascade)
User ──< GameReplay    (Cascade)
User ──< GameHistoryEntry (Cascade)

Game >── GameStatus    (Restrict: can't delete status if games use it)
Game >── GamePlatform  (SetNull: delete platform → null the FK)
Game >── GamePlayedStatus (SetNull)
Game ──< GamePlayWithMapping ──> GamePlayWith (many-to-many via join table)
Game ──< GameReplay    (Cascade: delete game → delete replays)

GameHistoryEntry >── Game (SetNull: delete game → history persists with null GameId)
```

### How Migrations Work

```bash
# Navigate to API project
cd "K:\Programacion\Main\Games Database\GamesDatabase.Api"

# Add a new migration
dotnet ef migrations add AddSomeFeature

# Apply pending migrations
dotnet ef database update

# Check for pending model changes
dotnet ef migrations has-pending-model-changes
```

At startup, `context.Database.Migrate()` runs automatically.

### How to Safely Update the Schema

1. Back up `gamesdatabase.db` before any schema change
2. Add a migration: `dotnet ef migrations add DescriptiveName`
3. Review the generated migration file — check for data loss risks
4. SQLite cannot rename or drop columns directly — EF Core uses table rebuild
5. Test on a copy of the database
6. Apply: `dotnet ef database update`
7. Verify the app starts and data is intact

---

## 6. Request Lifecycle

### Example: `GET /api/games?page=1&pageSize=20&statusId=3`

```
1. Frontend calls: GET /api/games?page=1&pageSize=20&statusId=3
   (GamesService.ts → axios GET with query params)

2. ASP.NET Core pipeline:
   → CORS middleware checks origin
   → ErrorHandlingMiddleware wraps try/catch
   → JWT Bearer authentication extracts user claims
   → UserContextMiddleware reads ClaimTypes.NameIdentifier
     and stores userId in HttpContext.Items["UserId"]
   → Authorization middleware checks [Authorize]

3. GamesController.GetGames(GameQueryParameters query)
   → Reads userId from HttpContext.GetUserId()
   → Calls _gameService.GetGamesAsync(query, userId)
     → GameService queries GamesDbContext.Games
       .Where(g => g.UserId == userId)
       .Include(g => g.Status) ... etc.
     → Applies traditional filters, view filters, sorting, pagination
     → Maps entities to DTOs
   → Returns the result

4. Returns PagedResult<GameDto>:
   {
     "data": [{ "id": 1, "name": "...", "statusName": "Playing", ... }],
     "totalCount": 150,
     "page": 1,
     "pageSize": 20,
     "totalPages": 8,
     "hasNextPage": true,
     "hasPreviousPage": false
   }

5. Frontend receives JSON → renders game grid
```

### Example: `PUT /api/games/42`

```
1. Frontend sends: PUT /api/games/42
   Body: raw JsonElement { "name": "Updated Name", "statusId": 3 }

2. GamesController.PutGame(42, JsonElement body)
   → Reads userId from HttpContext.GetUserId()
   → Calls _gameService.UpdateGameAsync(42, body, userId)
     → GameService loads game, applies changes, tracks history,
       calculates score, saves, and returns the DTO
   → Returns the result

3. Frontend receives updated GameDto → updates local state
```

---

## 7. Controllers Explained

### GamesController — The Core Controller

**Route:** `api/games` (via `[Route("api/[controller]")]`)

**Dependencies:** `IGameService`

Thin controller — all business logic is in `GameService`. Each action calls the corresponding service method and returns the result.

**Key endpoints:**

- `GetGames` — delegates to `_gameService.GetGamesAsync()` for filtering, sorting, pagination
- `PutGame` — delegates to `_gameService.UpdateGameAsync()` for partial updates and history tracking
- `PostGame` — delegates to `_gameService.CreateGameAsync()`
- `DeleteGame` — delegates to `_gameService.DeleteGameAsync()`
- `BulkUpdateGames` — delegates to `_gameService.BulkUpdateAsync()`

**Score calculation:** `Game.CalculateScore()` uses the formula: `10 × (Critic / 100) × (10 / (Story + 10))`.

### Catalog Controllers (Status, Platform, PlayWith, PlayedStatus, ReplayType)

All are thin controllers delegating to `ICatalogService`. They follow the same pattern:

```
GET  /api/{catalog}              → List with pagination/search
GET  /api/{catalog}/active       → List only active items
GET  /api/{catalog}/{id}         → Get by ID
POST /api/{catalog}              → Create (auto-assigns SortOrder)
PUT  /api/{catalog}/{id}         → Update
POST /api/{catalog}/reorder      → Reorder (receives ordered ID list)
DELETE /api/{catalog}/{id}       → Delete (checks referential integrity)
```

**Special features:**

- `GameStatusController` has `GET /special` and `POST /reassign-special` for managing default Playing/NotFulfilled statuses
- `GameReplayTypesController` has `GET /special` for the default Replay type

### DataExportController — Import/Export Engine

**Route:** `api/dataexport`

Thin controller — delegates to `IGameImportExportService`.

**Key operations:**

- `GET /full` — exports all games, catalogs, views, replays, and history as a single CSV
- `POST /full` — imports a CSV file with merge logic (creates/updates entities by name matching)
- `POST /selective-games-export` — exports selected games with configurable field inclusion
- `POST /selective-games-import` — imports selected games with per-field update modes (skip/overwrite/merge)
- `POST /update-image-urls` — scans NAS folders for game images, updates logo/cover URLs
- `GET /analyze-folders` — analyzes NAS export folder structure

### GameViewsController

**Route:** `api/gameviews`

Thin controller — delegates to `IGameViewService` for all view CRUD, duplication, and reorder operations.

### UsersController

**Route:** `api/users`

Thin controller — delegates to `IUserService` for user CRUD and role management. Login delegates to `IAuthService`.

### SteamController — Steam Integration

**Route:** `api/steam`

**Key operations:**

- `GET /library` — fetches user's Steam library via API
- `POST /import` — imports selected Steam games into the database
- `POST /sync` — syncs playtime and achievements for all linked games
- `GET /match-suggestions` — suggests matches between Steam library and existing games using Jaccard name similarity
- `GET /store/search` — searches the Steam store
- `GET /achievements/{gameId}` — returns cached achievements for a game

---

## 8. Services Explained

### GameService

**Interface:** `IGameService`

Handles all game CRUD operations. `GetGamesAsync` builds complex EF queries with traditional filters (status, platform, grade range, date ranges, replay filters), view-based filters (via `ViewFilterService`), diacritics-aware search, 15+ sort options, and pagination. `UpdateGameAsync` supports partial updates via `JsonElement`, tracks field changes via `GameHistoryService`, and recalculates scores. `CreateGameAsync`, `DeleteGameAsync`, and `BulkUpdateAsync` handle creation, deletion, and bulk mutations respectively.

### CatalogService

**Interface:** `ICatalogService`

Generic catalog CRUD + reorder for all catalog entity types (statuses, platforms, play-withs, played-statuses, replay types). Handles auto-assigning sort order, duplicate name detection, referential integrity checks on delete, and special status/replay type management.

### GameViewService

**Interface:** `IGameViewService`

Handles saved view CRUD, duplication, reorder, and filter/sort configuration validation.

### GameImportExportService

**Interface:** `IGameImportExportService`

Full CSV export/import with merge logic (creates/updates entities by name matching). Selective export/import with per-field configuration. Image URL detection, folder analysis, and duplicate game detection.

### UserService

**Interface:** `IUserService`

User CRUD, role checks, and centralized seed data logic. Handles user creation with default catalog seeding.

### AuthService

**Interface:** `IAuthService`

Handles user authentication. `AuthenticateAsync` looks up the user by username (case-insensitive), validates password with BCrypt, and generates a JWT token. Users with null `PasswordHash` (like the default Admin) can log in without a password.

### GameHistoryService

**Interface:** `IGameHistoryService`

Records game field changes. `RecordUpdatedAsync` receives the before-state and the `JsonElement` patch, builds diff entries for each changed field, and persists them. Enforces a max of 200 entries per game by deleting oldest entries.

### ViewFilterService

Applies `ViewConfiguration` filter groups to an `IQueryable<Game>`. Supports 18+ filter operators (Equals, Contains, GreaterThan, Between, IsNull, etc.) across 20+ fields (name, status, platform, dates, grade, etc.).

### BackupScheduleService

**Type:** Singleton + `IHostedService`

Runs on a background loop. Every 60 seconds, checks if any user's backup schedule is due. When triggered, calls `ZipExportService.BuildZipAsync`, writes the ZIP to the configured destination, and cleans up old backups based on `RetentionCount`.

### Steam Services

| Service             | Purpose                                                                 |
| ------------------- | ----------------------------------------------------------------------- |
| `SteamApiService`   | Calls Steam Web API — owned games, achievements, player summary         |
| `SteamStoreService` | Calls Steam Store API — app details, search with caching                |
| `SteamSyncService`  | Syncs Steam data to local DB — updates playtime, achievements, metadata |
| `SteamAuthService`  | Manages OpenID nonces for Steam login/link flow                         |

---

## 9. How to Add a New Feature

### Example: Adding a "Rating" Field to Games

#### Step 1 — Update the Entity

In `Domain/Entities/Game.cs`:

```csharp
public int? Rating { get; set; }
```

#### Step 2 — Update the Entity Configuration

In `Infrastructure/Persistence/Configurations/GameConfiguration.cs`:

```csharp
builder.Property(e => e.Rating).HasColumnName("rating");
```

#### Step 3 — Create a Migration

```bash
cd "K:\Programacion\Main\Games Database\GamesDatabase.Api"
dotnet ef migrations add AddRatingToGame
```

#### Step 4 — Update DTOs

In `Contracts/GameDTOs.cs`:

```csharp
// Add to GameDto
public int? Rating { get; set; }

// Add to GameCreateDto
public int? Rating { get; set; }

// Add to GameUpdateDto
public int? Rating { get; set; }
```

#### Step 5 — Update Mapping

In `Application/Mapping/MappingExtensions.cs`:

```csharp
// In ToDto():
Rating = game.Rating,

// In ToEntity():
Rating = dto.Rating,

// In UpdateEntity():
if (dto.Rating.HasValue) entity.Rating = dto.Rating.Value;
```

#### Step 6 — Update the Service

In `Application/Services/GameService.cs`, inside the update method, add handling for the new field:

```csharp
if (body.TryGetProperty("rating", out var ratingEl))
{
    game.Rating = ratingEl.ValueKind == JsonValueKind.Null ? null : ratingEl.GetInt32();
}
```

#### Step 7 — Update History Tracking

In `Application/Services/GameHistoryService.cs`, add field tracking in `BuildUpdatedEntries`:

```csharp
if (patch.TryGetProperty("rating", out var ratingEl))
{
    var newVal = ratingEl.ValueKind == JsonValueKind.Null ? (int?)null : ratingEl.GetInt32();
    if (before.Rating != newVal)
        entries.Add(Changed("Rating", before.Rating?.ToString(), newVal?.ToString(),
            $"Rating: {before.Rating?.ToString() ?? "—"} → {newVal?.ToString() ?? "—"}")!);
}
```

#### Step 8 — Update Export/Import (if needed)

If the field should be exported/imported, update `GameImportExportService.ExportFullDatabase` and `ImportFullDatabase` in `Application/Services/GameImportExportService.cs`.

#### Step 9 — Update Frontend

1. Add `rating` to TypeScript game types
2. Add UI controls for the field
3. Include in API service calls

#### Step 10 — Test

- Create a game with the new field
- Update the field
- Verify history records the change
- Export and re-import — verify the field is preserved

---

### Example: Adding a New Catalog Entity

If you need to add a new catalog type (e.g., "GameGenre"):

1. Create `Domain/Entities/GameGenre.cs` following the `GamePlatform` pattern
2. Add `DbSet<GameGenre>` to `GamesDbContext`
3. Create `Infrastructure/Persistence/Configurations/GameGenreConfiguration.cs` with `IEntityTypeConfiguration<GameGenre>`
4. Create a migration
5. Create DTOs in `Contracts/CatalogDTOs.cs`
6. Add mapping extension in `Application/Mapping/MappingExtensions.cs`
7. Register the new catalog type in `CatalogService` (or create a new service if the pattern differs)
8. Create `GameGenresController.cs` following any existing catalog controller pattern — delegate to `ICatalogService`
9. Add seeding in `DatabaseStartupHelper.SeedDefaultDataAsync` if defaults are needed
10. Update frontend services and types

---

## 10. How to Keep Future Code Clean and Human-Readable

### Naming

- Use specific names: `GetActiveGames()` not `GetGames()` with a comment "only active"
- Service names should be use-case focused: `GameImportService`, not `DataManager`
- DTOs: `CreateGameRequest` / `GameResponse`, not `GameDto` for both directions

### Comments

- Remove comments that repeat the code: `// Save changes` before `SaveChangesAsync()`
- Keep comments that explain **why**: `// SQLite cannot drop columns, so this migration rebuilds the table`
- Keep comments that explain **business rules**: `// Allow login without password for default Admin user`
- Write all comments in English

### Methods

- Each method should do one thing
- If a method needs section divider comments, it should be split
- Controller actions should be 10-40 lines: receive request → call service → return response

### Adding Endpoints

Follow this pattern — controllers should be thin and delegate to services:

```csharp
[HttpGet("{id:int}")]
public async Task<ActionResult<GameDto>> GetGame(int id)
{
    var userId = RequireUserId();
    var result = await _gameService.GetByIdAsync(id, userId);
    if (result == null) return NotFound();
    return Ok(result);
}
```

Do not put EF queries, business validation, or mapping logic in controller actions. All business logic belongs in the service layer (`Application/Services/`).

### How to Add a New Controller

1. Create the service interface in `Application/Interfaces/INewFeatureService.cs`
2. Create the service implementation in `Application/Services/NewFeatureService.cs`
3. Register the service in `Configuration/ServiceCollectionExtensions.cs`
4. Create the controller in `Controllers/` inheriting `BaseApiController`
5. Inject the service interface — keep controller actions to 5-15 lines

---

## 11. Configuration and Environment Variables

### appsettings.json Sections

| Section            | Purpose                                      |
| ------------------ | -------------------------------------------- |
| `CorsSettings`     | Allowed CORS origins array                   |
| `DatabaseSettings` | SQLite path, sensitive data logging          |
| `ExportSettings`   | CSV export path, delimiter, encoding         |
| `JwtSettings`      | JWT secret key, issuer, audience, expiration |
| `DataExport`       | Full export URL                              |
| `NetworkSync`      | NAS sync enabled, path, credentials          |
| `SteamSettings`    | Steam API key, base URLs, cache TTL          |

### Environment Variable Overrides

These environment variables override `appsettings.json` values:

| Variable                  | Config Key                      |
| ------------------------- | ------------------------------- |
| `JWT_SECRET_KEY`          | `JwtSettings:SecretKey`         |
| `JWT_ISSUER`              | `JwtSettings:Issuer`            |
| `JWT_AUDIENCE`            | `JwtSettings:Audience`          |
| `JWT_EXPIRATION_MINUTES`  | `JwtSettings:ExpirationMinutes` |
| `NETWORK_SYNC_ENABLED`    | `NetworkSync:Enabled`           |
| `NETWORK_SYNC_PATH`       | `NetworkSync:NetworkPath`       |
| `NETWORK_SYNC_USERNAME`   | `NetworkSync:Username`          |
| `NETWORK_SYNC_PASSWORD`   | `NetworkSync:Password`          |
| `STEAM_API_KEY`           | `SteamSettings:ApiKey`          |
| `STEAM_CALLBACK_BASE_URL` | `SteamSettings:CallbackBaseUrl` |
| `STEAM_FRONTEND_BASE_URL` | `SteamSettings:FrontendBaseUrl` |
| `GDB_DESKTOP`             | Desktop mode detection          |

### Docker Deployment

The `Dockerfile` builds a multi-stage image. The `docker-compose.yml` defines the full stack. Database is stored at a mounted volume. Environment variables are set in `.env`.

### CasaOS Deployment

Follows the Docker pattern with CasaOS-specific labels and port mappings.

---

## 12. Authentication and Authorization

### Flow

1. User calls `POST /api/users/login` with `{ username, password }`
2. `AuthService.AuthenticateAsync` finds user by username (case-insensitive)
3. If user has `PasswordHash`, validates with BCrypt. If null, allows passwordless login.
4. Generates JWT token with claims: `NameIdentifier` (userId), `Name` (username), `Role`
5. Returns `{ userId, username, role, token, steamId?, steamNickname?, steamAvatarUrl? }`
6. Frontend stores token, sends as `Authorization: Bearer {token}` on all requests

### Middleware

- `UserContextMiddleware` extracts `UserId` from JWT claims and stores in `HttpContext.Items["UserId"]`
- `BaseApiController.RequireUserId()` reads from `HttpContext.Items`
- All authenticated controllers inherit `BaseApiController` which has `[Authorize]`

### Roles

- `Admin` (1) — full access, can manage all users, see all user data
- `Standard` (0) — can only access own data

### Multi-User Data Isolation

Every query filters by `UserId`:

```csharp
var games = await _context.Games
    .Where(g => g.UserId == userId)
    .ToListAsync();
```

Each user has their own catalogs (statuses, platforms, etc.). Deleting a user cascades to all their data.

---

## 13. Error Handling

`ErrorHandlingMiddleware` catches all unhandled exceptions and returns consistent JSON:

```json
{
  "statusCode": 409,
  "message": "Ya existe un elemento con este nombre",
  "details": "El nombre debe ser único.",
  "timestamp": "2026-05-21T10:30:00Z"
}
```

### Exception Mapping

| Exception                                          | HTTP Status               | When                        |
| -------------------------------------------------- | ------------------------- | --------------------------- |
| `DbUpdateException` → `SqliteException` (UNIQUE)   | 409 Conflict              | Duplicate name/value        |
| `DbUpdateException` → `SqliteException` (FK)       | 400 Bad Request           | Invalid foreign key         |
| `DbUpdateException` → `SqliteException` (NOT NULL) | 400 Bad Request           | Missing required field      |
| `ArgumentException`                                | 400 Bad Request           | Invalid input data          |
| `UnauthorizedAccessException`                      | 401 Unauthorized          | Missing/invalid permissions |
| `KeyNotFoundException`                             | 404 Not Found             | Resource doesn't exist      |
| Any other exception                                | 500 Internal Server Error | Unexpected error            |

---

## 14. Key Business Rules

### Score Calculation

```
Score = 10 × (Critic / 100) × (10 / (Story + 10))
```

Where `Critic` is a 0-100 critic score and `Story` is a 0-100 story hours value. Both must be non-null for the score to be calculated. Called via `game.CalculateScore()`.

### Special Status Types

- `Playing` — exactly one status per user must be the default "Playing" status
- `NotFulfilled` — exactly one status per user must be the default "Not Fulfilled" status
- Enforced via unique filtered index: `(UserId, StatusType, IsDefault) WHERE is_default = 1`
- Reassignment endpoint: `POST /api/gamestatus/reassign-special`

### Special Replay Type

- Exactly one replay type per user must be the default "Replay" type
- When deleting a replay type, all its replays are reassigned to the special Replay type

### Export Tracking

Games and views track `ModifiedSinceExport`. When any field other than `ModifiedSinceExport` or `UpdatedAt` is changed, `ModifiedSinceExport` is set to `true`. This enables incremental exports.

### History Tracking

Every field change on a game creates a `GameHistoryEntry` with the old and new values. Max 200 entries per game — oldest are pruned when the limit is exceeded.

### Steam Playtime Priority

When `ManualPlaytimeMinutes` is set, it takes priority over `SteamPlaytimeForever` in the frontend display.

---

## 15. Common Tasks Reference

### Running Locally

```bash
cd "K:\Programacion\Main\Games Database\GamesDatabase.Api"
dotnet run
# API available at https://localhost:7245 or http://localhost:5011
# Swagger at /swagger
```

### Adding a Migration

```bash
dotnet ef migrations add DescriptiveName
dotnet ef database update
```

### Checking for Pending Changes

```bash
dotnet ef migrations has-pending-model-changes
```

### Building for Docker

```bash
docker build -t gamesdatabase-api .
```

### Resetting the Database

Delete `gamesdatabase.db` and restart the app. Migrations will recreate the schema and seeding will create the default Admin user with default catalogs.

### Debugging EF Queries

Set `EnableSensitiveDataLogging: true` in `DatabaseSettings`. SQL queries are logged to console via `options.LogTo(Console.WriteLine, LogLevel.Information)`.
