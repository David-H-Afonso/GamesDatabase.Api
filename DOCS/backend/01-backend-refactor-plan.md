# Backend Refactor Plan — Games Database API

This document is specific to the Games Database API project. It analyzes the current state of the backend, identifies problems, and proposes a phased refactor plan.

---

## Implementation Summary

**Status:** Phases 0–8 completed (Phase 5 intentionally skipped). Phases 9–10 remain.

The backend refactor was executed between May 2026, transforming the codebase from fat-controller architecture to a clean service-oriented design. Key outcomes:

- **Program.cs** reduced from ~640 lines to ~80 lines via `ServiceCollectionExtensions` and `DatabaseStartupHelper`
- **Folder structure** reorganized: `Models/` → `Domain/Entities/`, `Data/` → `Infrastructure/Persistence/`, `DTOs/` → `Contracts/`, `Helpers/` → `Common/` + `Application/Mapping/`, services → `Application/Services/` + `Application/Interfaces/`
- **DbContext** cleaned: entity configurations extracted into 17 `IEntityTypeConfiguration<T>` files under `Infrastructure/Persistence/Configurations/`
- **Service layer** fully extracted: `GameService`, `CatalogService`, `GameViewService`, `GameImportExportService`, `UserService` now handle all business logic; controllers are thin delegates
- **Phase 5 (DTO renaming) was intentionally SKIPPED** to preserve frontend compatibility — DTOs keep their original names (`GameDto`, `GameCreateDto`, etc.)
- **Error handling** standardized, Spanish messages translated to English, validation attributes added
- **Code style** cleaned: unnecessary comments removed, all comments in English
- **Security** hardened: default JWT secret removed, `X-User-Id` restricted to development, `[Authorize]` audited

**Remaining work:** Phase 9 (Tests) and Phase 10 (Final verification).

---

## 1. Executive Summary

Games Database is a personal game collection management API built with ASP.NET Core 9, Entity Framework Core, and SQLite. It supports multi-user authentication via JWT, Steam integration (library import, achievements, playtime sync), custom game views with complex filtering, CSV/ZIP import/export, scheduled backups, network sync to NAS, and an image optimization proxy.

**Technology stack:** .NET 9, ASP.NET Core, EF Core 9, SQLite, JWT (BCrypt), ImageSharp, CsvHelper, Swagger/OpenAPI.

**Current architectural quality:** Functional and feature-rich, but architecturally inconsistent. The API works and the frontend consumes it correctly, but most business logic lives directly inside controllers. There are 18 controllers totaling ~5,900 lines, while the service layer is thin and only covers auth, history tracking, export, Steam, and backups. The remaining domains (games CRUD, catalogs, views) have no service extraction at all.

**Main problems:**

- Fat controllers — `GamesController` (1,020 lines), `DataExportController` (1,350 lines), and `SteamController` (870 lines) contain heavy inline business logic.
- `Program.cs` is ~640 lines with inline schema repair SQL, seeding, env-var loading, and all service registration.
- DbContext has a monolithic `OnModelCreating` (~400 lines) and a `UpdateTimestamps` method with repetitive type-checking.
- DTOs use `class` everywhere instead of records. DTO naming is inconsistent (`GameDto` vs `GameCreateDto` vs `GameStatusDto`).
- Some controllers bypass `BaseApiController` and miss `[Authorize]` at class level.
- `PutGame` uses raw `JsonElement` instead of a typed DTO.
- No tests exist.

**Main opportunities:**

- Extract game CRUD, catalog, view, and import/export logic into services.
- Clean up `Program.cs` with extension methods.
- Extract entity configurations from the monolithic `OnModelCreating`.
- Standardize DTO naming and use records.
- Remove unnecessary comments and improve naming for human-readable code.

**Recommended direction:** Gradual refactoring following a phased approach — extract services first, then reorganize folders, then clean DTOs and Program.cs. Keep the frontend working at every step.

---

## 2. Current Backend Structure

```
GamesDatabase.Api/
  Configuration/
    CorsSettings.cs
    DatabaseSettings.cs
    DataExportOptions.cs
    ExportSettings.cs
    JwtSettings.cs
    NetworkSyncOptions.cs
    SteamSettings.cs
  Controllers/
    BackupScheduleController.cs          (~320 lines)
    BaseApiController.cs                 (~27 lines)
    ConfigController.cs                  (~31 lines)
    DataExportController.cs              (~1,350 lines) ← largest
    ExportController.cs                  (~210 lines)
    GameHistoryController.cs             (~149 lines)
    GamePlatformsController.cs           (~246 lines)
    GamePlayedStatusController.cs        (~198 lines)
    GamePlayWithController.cs            (~193 lines)
    GameReplaysController.cs             (~119 lines)
    GameReplayTypesController.cs         (~186 lines)
    GamesController.cs                   (~1,020 lines) ← second largest
    GameStatusController.cs              (~380 lines)
    GameViewsController.cs               (~470 lines)
    ImageProxyController.cs              (~210 lines)
    SteamAuthController.cs               (~210 lines)
    SteamController.cs                   (~870 lines)
    UsersController.cs                   (~310 lines)
  Data/
    GamesDbContext.cs                    (~500 lines)
  DTOs/
    CatalogDTOs.cs
    GameDTOs.cs
    GameViewDTOs.cs
    ReplayAndHistoryDTOs.cs
    SelectiveImportExportDTOs.cs
    SteamDTOs.cs
    UserDTOs.cs
  Helpers/
    FolderNameHelper.cs
    GameDateNormalizer.cs
    GameViewMappingExtensions.cs
    HttpContextHelper.cs
    MappingExtensions.cs
    NetworkPathHelper.cs
    QueryHelpers.cs
  Middleware/
    ErrorHandlingMiddleware.cs
    UserContextMiddleware.cs
  Migrations/                            (17 migrations)
  Models/
    BackupSchedule.cs
    ExportRecord.cs
    Game.cs
    GameExportCache.cs
    GameHistoryEntry.cs
    GamePlatform.cs
    GamePlayedStatus.cs
    GamePlayWith.cs
    GamePlayWithMapping.cs
    GameReplay.cs
    GameReplayType.cs
    GameStatus.cs
    GameView.cs
    GameViewExportCache.cs
    SteamAchievement.cs
    SteamAppCache.cs
    SteamMatchDismissal.cs
    User.cs
  Services/
    AuthService.cs / IAuthService.cs
    BackupScheduleService.cs
    GameHistoryService.cs / IGameHistoryService.cs
    NetworkSyncService.cs / INetworkSyncService.cs
    ViewFilterService.cs
    ZipExportService.cs / IZipExportService.cs
    FolderAnalysisResult.cs
    NetworkSyncResult.cs
    ZipExportResult.cs
    Steam/
      ISteamApiService.cs / SteamApiService.cs
      ISteamAuthService.cs / SteamAuthService.cs
      ISteamStoreService.cs / SteamStoreService.cs
      ISteamSyncService.cs / SteamSyncService.cs
  Program.cs                             (~640 lines)
```

### Folder Responsibilities (Current)

| Folder           | Current Role                                                            |
| ---------------- | ----------------------------------------------------------------------- |
| `Configuration/` | Strongly-typed Options classes — well structured                        |
| `Controllers/`   | HTTP endpoints + most business logic, EF queries, validation, mapping   |
| `Data/`          | DbContext with monolithic `OnModelCreating` and timestamp tracking      |
| `DTOs/`          | Request/response DTOs grouped by domain — class-based                   |
| `Helpers/`       | Mapping extensions, query parameters, date normalization, network paths |
| `Middleware/`    | Global error handling + user context extraction from JWT                |
| `Models/`        | EF Core entities                                                        |
| `Services/`      | Only auth, history, export, backup, and Steam services                  |

---

## 3. Current Request Flow

```
Frontend (React) → HTTP Request
  → CORS middleware
  → ErrorHandlingMiddleware
  → JWT Authentication
  → UserContextMiddleware (extracts UserId into HttpContext.Items)
  → Authorization
  → Controller action
    → Direct DbContext queries (most controllers)
    → OR Service call → DbContext → SQLite
  → Manual mapping (entity → DTO)
  → JSON response → Frontend
```

Most controllers skip the service layer entirely. Only Export, Auth, History, Backup, and Steam operations are delegated to services.

---

## 4. Current Database and EF Core Setup

### DbContext

- Located in `Data/GamesDbContext.cs`
- Contains 17 `DbSet<T>` properties
- `OnModelCreating` is ~400 lines of inline fluent configuration — no `IEntityTypeConfiguration<T>` classes
- `UpdateTimestamps()` overrides `SaveChanges`/`SaveChangesAsync` to set `CreatedAt`/`UpdatedAt` and track export modification flags
- Lazy loading is explicitly disabled

### Connection

- SQLite at `../gamesdatabase.db` (relative, resolved to absolute in `Program.cs`)
- Connection string built manually: `Data Source={databasePath}`
- `DatabaseSettings` Options class configures path and sensitive data logging

### Entities (18 total)

| Entity                | Purpose                                                               |
| --------------------- | --------------------------------------------------------------------- |
| `User`                | Multi-user accounts with JWT auth, Steam linking                      |
| `Game`                | Core entity — game entries with scores, dates, images, Steam metadata |
| `GamePlatform`        | User-customizable platforms (Steam, Epic, etc.)                       |
| `GameStatus`          | User-customizable statuses with special types (Playing, NotFulfilled) |
| `GamePlayWith`        | Who the user plays with (Solo, Friends, Family)                       |
| `GamePlayWithMapping` | Many-to-many join table (Game ↔ PlayWith)                             |
| `GamePlayedStatus`    | Completion-level status (None, Some, Completed, etc.)                 |
| `GameView`            | Saved filter/sort configurations as JSON                              |
| `GameReplay`          | Replay/DLC/expansion entries per game                                 |
| `GameReplayType`      | Replay category types with special Replay type                        |
| `GameHistoryEntry`    | Change tracking log per game field                                    |
| `GameExportCache`     | Tracks per-game export state and image download status                |
| `GameViewExportCache` | Tracks per-view export state                                          |
| `BackupSchedule`      | Scheduled backup configuration per user                               |
| `SteamAchievement`    | Cached Steam achievements per user/game                               |
| `SteamAppCache`       | Cached Steam store app metadata                                       |
| `SteamMatchDismissal` | Dismissed Steam match suggestions                                     |
| `ExportRecord`        | CsvHelper model for CSV export rows (not a DB entity)                 |

### Migrations

17 migrations from October 2025 to May 2026. Migrations are applied at startup via `context.Database.Migrate()`.

### Schema Repair

`Program.cs` contains an `EnsureCompatibilitySchema` method (~80 lines) that uses raw SQL `ALTER TABLE` and `CREATE TABLE IF NOT EXISTS` to add missing columns and tables. This acts as a safety net for databases that may have missed migrations.

### Seeding

Two seeding methods in `Program.cs`:

- `SeedDefaultDataAsync` — creates Admin user + default platforms, statuses, play-withs, played-statuses, replay types
- `SeedMissingReplayTypesAsync` — adds replay types for users who don't have any

### Risks

- Schema repair SQL in `Program.cs` duplicates what migrations should handle, making the source of truth ambiguous
- `UpdateTimestamps()` in DbContext uses type-checking with `is` pattern matching for 4 entity types — fragile and repetitive
- The `IsManuallyCompleted` column name is `"IsManuallyCompleted"` (PascalCase) while all other columns use snake_case — inconsistency

---

## 5. Current API Surface

### Game Management (`GamesController`)

| Method | Route             | Purpose                                                      |
| ------ | ----------------- | ------------------------------------------------------------ |
| GET    | `/api/games`      | List games with filtering, sorting, pagination, view support |
| GET    | `/api/games/{id}` | Get single game with relationships                           |
| POST   | `/api/games`      | Create game                                                  |
| PUT    | `/api/games/{id}` | Update game (uses raw `JsonElement`)                         |
| DELETE | `/api/games/{id}` | Delete game                                                  |
| PATCH  | `/api/games/bulk` | Bulk update multiple games                                   |

**Concerns:** 1,020-line controller. `GetGames` builds complex EF queries inline (~300 lines) including view filter deserialization, diacritics-aware search, and 15+ sort options. `PutGame` manually parses `JsonElement` fields instead of using a typed DTO.

### Catalog Controllers (Status, Platforms, PlayWith, PlayedStatus, ReplayTypes)

Each follows the same pattern: GET all, GET active, GET by id, POST create, PUT update, POST reorder, DELETE. All inject `GamesDbContext` directly and contain inline CRUD logic.

**Concerns:** Nearly identical code duplicated across 5 controllers. Could share a generic catalog service.

### Game Views (`GameViewsController`)

CRUD for saved filter/sort configurations with JSON validation, duplication, and reorder support.

**Concerns:** Contains a nested `ValidationResult` class. Uses raw `JsonElement` for config updates.

### Replays (`GameReplaysController`)

Nested routes under `api/games/{gameId}/replays`. Cleanest controller — only 119 lines.

### History (`GameHistoryController`)

Browse and delete game change history. Admin endpoint for cross-user history.

**Concerns:** Admin role check done inline via DB query instead of policy/attribute.

### Users (`UsersController`)

Login, CRUD, password change. Inherits `ControllerBase` directly (not `BaseApiController`).

**Concerns:** Contains `SeedUserDefaultDataAsync` (~80 lines of hardcoded seed data) duplicating `Program.cs` seed logic. Inline DTO mapping instead of using extension methods.

### Export/Import (`DataExportController`, `ExportController`)

Full CSV export/import, selective export/import, ZIP export, network sync, image URL update, folder analysis, duplicate detection.

**Concerns:** `DataExportController` is 1,350 lines — the largest file. `ImportFullDatabase` is a ~400-line method with inline merge/upsert logic for every entity type. Contains inline model classes. Static `HttpClient` for image probing.

### Backup (`BackupScheduleController`)

Scheduled backup management with admin endpoints.

**Concerns:** Uses `Task.Run` with `HttpContext.RequestServices.CreateScope()` which is unsafe after request ends. Inline DTO records.

### Steam Integration (`SteamController`, `SteamAuthController`)

Library import, achievement sync, match suggestions, store search, OpenID auth flow.

**Concerns:** `SteamController` has 18 endpoints. Name matching algorithm (Jaccard similarity) is inline. Good service delegation for import/sync but match logic is inline.

### Image Proxy (`ImageProxyController`)

Resizes and converts images to WebP with disk caching and ETag support.

**Concerns:** Standalone controller with no base class. Path traversal protection is implemented correctly.

### Configuration (`ConfigController`)

Returns network sync config. Smallest controller (31 lines). Correctly omits credentials.

---

## 6. Current Problems and Risks

| Area           | Problem                                                                                                                | Why it matters                                                       | Severity | Recommended action                                                                |
| -------------- | ---------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------- | -------- | --------------------------------------------------------------------------------- |
| Controllers    | Fat controllers — `GamesController` (1,020 lines), `DataExportController` (1,350 lines), `SteamController` (870 lines) | Violates SRP, hard to test, hard to maintain, not recruiter-friendly | High     | Extract business logic into services                                              |
| Controllers    | 15 of 18 controllers inject `GamesDbContext` directly                                                                  | Couples HTTP layer to persistence, makes testing harder              | Medium   | Controllers should only call services                                             |
| Controllers    | Inconsistent base class — 3 controllers skip `BaseApiController`                                                       | Missing `[Authorize]` and user ID helpers                            | Medium   | All controllers should inherit `BaseApiController` or have explicit `[Authorize]` |
| Controllers    | Missing `[Authorize]` at class level on 5 controllers                                                                  | Endpoints may be accidentally unprotected                            | High     | Add `[Authorize]` or verify intentional `[AllowAnonymous]`                        |
| Controllers    | `PutGame` uses raw `JsonElement` instead of typed DTO                                                                  | Hard to validate, error-prone, looks unprofessional                  | Medium   | Use the existing `GameUpdateDto` or a patch DTO                                   |
| Controllers    | Inline DTO/model classes in several controllers                                                                        | Code organization violation, hard to find types                      | Low      | Move to `DTOs/` or `Contracts/` folder                                            |
| Program.cs     | 640 lines with inline schema repair SQL, seeding, env-var loading                                                      | Hard to read, hard to maintain, not recruiter-friendly               | High     | Extract into extension methods                                                    |
| Program.cs     | Schema repair (`EnsureCompatibilitySchema`) duplicates migration work                                                  | Ambiguous source of truth for schema                                 | Medium   | Remove once all deployments have run migrations                                   |
| Program.cs     | Seed data duplicated in `UsersController.SeedUserDefaultDataAsync`                                                     | Two places to maintain same seed data                                | Medium   | Centralize seeding logic                                                          |
| DbContext      | Monolithic `OnModelCreating` (~400 lines)                                                                              | Hard to read, hard to maintain                                       | Medium   | Extract `IEntityTypeConfiguration<T>` classes                                     |
| DbContext      | `UpdateTimestamps()` uses repetitive type-checking                                                                     | Fragile, must update for every new timestamped entity                | Low      | Use a shared interface `IHasTimestamps`                                           |
| Models         | `IsManuallyCompleted` column uses PascalCase while all others use snake_case                                           | Schema inconsistency                                                 | Low      | Add explicit snake_case mapping in config                                         |
| DTOs           | All DTOs are classes instead of records                                                                                | More verbose than necessary, mutable                                 | Low      | Migrate to records gradually                                                      |
| DTOs           | Inconsistent naming — `GameDto` vs request/response pattern                                                            | Doesn't match architecture standard                                  | Medium   | Rename to `GameResponse`, `CreateGameRequest`, etc.                               |
| Helpers        | `MappingExtensions.cs` lives in `Helpers/` not `Application/Mapping/`                                                  | Doesn't match target folder structure                                | Low      | Move when reorganizing folders                                                    |
| Services       | No `IViewFilterService` interface file — only implementation                                                           | Missing interface for DI testability                                 | Low      | Verify interface exists; add if missing                                           |
| Security       | Default JWT secret in `appsettings.json`                                                                               | Security risk if deployed without override                           | High     | Remove default secret, require env var                                            |
| Security       | `X-User-Id` header fallback in `UserContextMiddleware`                                                                 | Allows impersonation without auth in any environment                 | High     | Restrict to development only                                                      |
| Testing        | No tests exist                                                                                                         | No safety net for refactoring, not recruiter-ready                   | High     | Add tests after service extraction                                                |
| Error handling | Spanish error messages mixed with English code                                                                         | Inconsistent localization                                            | Low      | Standardize language (English or add i18n)                                        |
| Code style     | Some comments in Spanish ("Configuración por defecto", "Desactivar lazy loading")                                      | Inconsistent language                                                | Low      | Standardize to English                                                            |
| Code style     | XML doc comments on entities that add no value                                                                         | Noise, looks generated                                               | Low      | Remove or replace with useful comments                                            |

---

## 7. Code Style and Comment Cleanup

### Unnecessary Comments

The codebase has a moderate amount of unnecessary comments, especially in models and DbContext:

**Remove — repeat the code:**

```csharp
// Desactivar lazy loading para evitar ciclos
ChangeTracker.LazyLoadingEnabled = false;

// Image paths for logo and cover
public string? Logo { get; set; }
public string? Cover { get; set; }

// Price comparison fields
public bool? IsCheaperByKey { get; set; }

// Steam integration
public int? SteamAppId { get; set; }

// Export tracking
public bool ModifiedSinceExport { get; set; } = true;

// Audit fields
public DateTime CreatedAt { get; set; }

// Navigation properties simplificadas (solo datos básicos)
public string? StatusName { get; set; }

// Configure User entity
modelBuilder.Entity<User>(entity => { ... });

// Configure Game entity
modelBuilder.Entity<Game>(entity => { ... });

// Add services to the container.
builder.Services.AddControllers()

// Configure CORS
var corsSettings = ...

// Configure Entity Framework
var databaseSettings = ...

// Configurar autenticación JWT en Swagger
c.AddSecurityDefinition(...)

// DTO para crear/actualizar sin manejar manualmente el SortOrder
public class GamePlatformCreateDto { }

// SortOrder se asigna automáticamente (in DTO)

// ID se toma de la URL, no del cuerpo (in DTO)

// Ordered list of status IDs in the desired order (first = lowest SortOrder)
public List<int> OrderedIds { get; set; }
```

**Keep — explain intent or risk:**

```csharp
// Only mark as modified if it's not just the ModifiedSinceExport flag being updated
// (in UpdateTimestamps — explains non-obvious filtering logic)

// ON DELETE SET NULL — historial persiste aunque el juego se borre
// (explains why SetNull instead of Cascade)

// Manual playtime override in minutes. When present, it takes priority over Steam time.
// (explains business priority rule)

// Allow login without password for users with null PasswordHash (like default Admin)
// (explains security-relevant business decision)

// SQLite does not support dropping this column directly...
// (any SQLite limitation comments)

// Keep deleted users queryable because historical audit entries reference them
// (if applicable)
```

### Mixed Language

Comments alternate between Spanish and English. Spanish examples: "Configuración por defecto en caso de que no haya orígenes configurados", "Desactivar lazy loading para evitar ciclos", "Relación muchos-a-muchos con PlayWith", "Calcula el score automáticamente", "Garantiza que solo haya un tipo especial Replay por usuario".

**Recommendation:** Standardize all comments to English to match the codebase language.

### AI-Looking Patterns

- Section comments in `OnModelCreating`: `// Configure User entity`, `// Configure Game entity`, etc. for every entity — obvious boilerplate
- `// ─── private helpers ────` section dividers in `GameHistoryService` suggest the method is large enough to split
- DTOs have section comments grouping fields (`// Price comparison fields`, `// Steam integration`, `// Audit fields`) — the grouping should come from property ordering, not comments
- `Program.cs` has comment headers for every block (`// Configure CORS`, `// Configure Entity Framework`, `// Override JWT settings...`) — should be extension methods instead

### Naming Improvements

| Current                 | Suggested                     | Reason                              |
| ----------------------- | ----------------------------- | ----------------------------------- |
| `GameDto`               | `GameResponse`                | Matches request/response convention |
| `GameCreateDto`         | `CreateGameRequest`           | Matches standard                    |
| `GameUpdateDto`         | `UpdateGameRequest`           | Matches standard                    |
| `GamePlatformDto`       | `GamePlatformResponse`        | Consistency                         |
| `Helpers/` folder       | `Application/Mapping/`        | Matches architecture standard       |
| `Data/` folder          | `Infrastructure/Persistence/` | Matches architecture standard       |
| `Models/` folder        | `Domain/Entities/`            | Matches architecture standard       |
| `GamePlayWiths` (DbSet) | `GamePlayWithOptions` or keep | Confusing plural of compound noun   |
| `UpdateTimestamps()`    | Keep but refactor             | Extract interface `IHasTimestamps`  |

---

## 8. Proposed Target Architecture

```
GamesDatabase.Api/
  Controllers/
    BackupScheduleController.cs
    BaseApiController.cs
    ConfigController.cs
    DataExportController.cs
    ExportController.cs
    GameHistoryController.cs
    GamePlatformsController.cs
    GamePlayedStatusController.cs
    GamePlayWithController.cs
    GameReplaysController.cs
    GameReplayTypesController.cs
    GamesController.cs
    GameStatusController.cs
    GameViewsController.cs
    ImageProxyController.cs
    SteamAuthController.cs
    SteamController.cs
    UsersController.cs
  Contracts/
    Requests/
      CreateGameRequest.cs
      UpdateGameRequest.cs
      BulkUpdateGameRequest.cs
      CreateGameViewRequest.cs
      UpdateGameViewRequest.cs
      ... (one file per request DTO or grouped by domain)
    Responses/
      GameResponse.cs
      GameViewResponse.cs
      GamePlatformResponse.cs
      ... (one file per response DTO or grouped by domain)
    External/
      SteamModels.cs
  Domain/
    Entities/
      Game.cs
      User.cs
      GameView.cs
      GamePlatform.cs
      GameStatus.cs
      GamePlayWith.cs
      GamePlayWithMapping.cs
      GamePlayedStatus.cs
      GameReplay.cs
      GameReplayType.cs
      GameHistoryEntry.cs
      GameExportCache.cs
      GameViewExportCache.cs
      BackupSchedule.cs
      SteamAchievement.cs
      SteamAppCache.cs
      SteamMatchDismissal.cs
    Enums/
      UserRole.cs
      SpecialStatusType.cs
      SpecialReplayType.cs
      FilterOperator.cs
      FilterField.cs
  Application/
    Interfaces/
      IGameService.cs
      ICatalogService.cs
      IGameViewService.cs
      IGameImportExportService.cs
      IAuthService.cs
      IGameHistoryService.cs
      IZipExportService.cs
      INetworkSyncService.cs
      ISteamApiService.cs
      ISteamAuthService.cs
      ISteamStoreService.cs
      ISteamSyncService.cs
    Services/
      GameService.cs
      CatalogService.cs
      GameViewService.cs
      GameImportExportService.cs
      AuthService.cs
      GameHistoryService.cs
      ZipExportService.cs
      NetworkSyncService.cs
      BackupScheduleService.cs
      Steam/
        SteamApiService.cs
        SteamAuthService.cs
        SteamStoreService.cs
        SteamSyncService.cs
    Mapping/
      GameMappingExtensions.cs
      GameViewMappingExtensions.cs
      CatalogMappingExtensions.cs
      UserMappingExtensions.cs
  Infrastructure/
    Persistence/
      GamesDbContext.cs
      Configurations/
        GameConfiguration.cs
        UserConfiguration.cs
        GameViewConfiguration.cs
        GameStatusConfiguration.cs
        GamePlatformConfiguration.cs
        ... (one per entity)
      Migrations/
        ... (existing migrations, untouched)
  Configuration/
    CorsSettings.cs
    DatabaseSettings.cs
    DataExportOptions.cs
    ExportSettings.cs
    JwtSettings.cs
    NetworkSyncOptions.cs
    SteamSettings.cs
    ServiceCollectionExtensions.cs
  Common/
    QueryParameters.cs
    PagedResult.cs
    GameDateNormalizer.cs
    FolderNameHelper.cs
    NetworkPathHelper.cs
  Middleware/
    ErrorHandlingMiddleware.cs
    UserContextMiddleware.cs
  Program.cs
  appsettings.json
  appsettings.Development.json
```

---

## 9. Refactor Phases

### Phase 0 — Baseline Safety

**Goal:** Ensure the app works before any changes.

**Actions:**

- Verify the app builds and runs locally
- Test key frontend flows (login, list games, create game, export)
- Back up the SQLite database
- Commit current state as a "pre-refactor" baseline

**Risks:** None — no code changes.

**How to verify:** App starts, frontend renders, CRUD operations work.

---

### Phase 1 — Program.cs Cleanup

**Goal:** Make `Program.cs` small and readable by extracting into extension methods.

**Files affected:**

- `Program.cs`
- New: `Configuration/ServiceCollectionExtensions.cs`
- New: `Configuration/WebApplicationExtensions.cs`

**Actions:**

- Extract env-var loading into a helper method
- Extract Options registration into `AddOptionsConfiguration()`
- Extract DbContext setup into `AddPersistence()`
- Extract service registration into `AddApplicationServices()`
- Extract CORS setup into `AddCorsConfiguration()`
- Extract JWT/Auth setup into `AddJwtAuthentication()`
- Extract Swagger setup into `AddApiDocumentation()`
- Extract seeding into a `DatabaseInitializer` or startup extension
- Extract schema repair into its own class (mark for future removal)
- Keep `Program.cs` under 50 lines

**Risks:** Low — only reorganizing existing code, no logic changes.

**How to verify:** App starts identically. Swagger works. Frontend works.

**Frontend changes needed:** None.

---

### Phase 2 — Folder Structure Reorganization

**Goal:** Move files to match the target folder structure.

**Files affected:** All model, DTO, helper, and data files (moved, not modified).

**Actions:**

- Move `Models/` → `Domain/Entities/`
- Move `Data/GamesDbContext.cs` → `Infrastructure/Persistence/GamesDbContext.cs`
- Move `DTOs/` → `Contracts/` (split into `Requests/` and `Responses/`)
- Move `Helpers/MappingExtensions.cs` → `Application/Mapping/GameMappingExtensions.cs`
- Move `Helpers/GameViewMappingExtensions.cs` → `Application/Mapping/GameViewMappingExtensions.cs`
- Move `Helpers/QueryHelpers.cs` → `Common/QueryParameters.cs`
- Move `Helpers/HttpContextHelper.cs` → `Common/HttpContextExtensions.cs`
- Move remaining helpers to `Common/`
- Extract enums from entity files into `Domain/Enums/`
- Move service interfaces to `Application/Interfaces/`
- Move service implementations to `Application/Services/`
- Update all `using` statements

**Risks:** Low — namespace changes may cause build errors, but IDE refactoring handles this.

**How to verify:** App builds, all tests pass (if any), frontend works.

**Frontend changes needed:** None — routes and JSON shapes don't change.

---

### Phase 3 — DbContext Cleanup

**Goal:** Extract entity configurations and simplify the DbContext.

**Files affected:**

- `Infrastructure/Persistence/GamesDbContext.cs`
- New: `Infrastructure/Persistence/Configurations/` (one file per entity)

**Actions:**

- Extract each entity's fluent config into `IEntityTypeConfiguration<T>` classes
- Replace `OnModelCreating` body with `modelBuilder.ApplyConfigurationsFromAssembly(...)`
- Create `IHasTimestamps` interface for entities with `CreatedAt`/`UpdatedAt`
- Simplify `UpdateTimestamps()` using the interface
- Fix `IsManuallyCompleted` column name to snake_case `is_manually_completed` (requires migration)

**Risks:**

- Column rename for `IsManuallyCompleted` needs a migration and data check
- Verify entity configurations produce identical SQL

**How to verify:** Run `dotnet ef migrations has-pending-model-changes`. Compare generated SQL with before. Test CRUD operations.

**Frontend changes needed:** None.

---

### Phase 4 — Service Layer Extraction

**Goal:** Move business logic from controllers to services. This is the most impactful phase.

**Files affected:**

- All controllers (thinned)
- New services: `GameService`, `CatalogService`, `GameViewService`, `GameImportExportService`, `UserService`

**Actions:**

#### 4a — GameService

- Extract from `GamesController`: `GetGames` query building, `PostGame` create logic, `PutGame` update logic, `DeleteGame`, `BulkUpdateGames`
- Move view-filter integration, diacritics search, sorting, pagination into `GameService`
- Controller becomes: receive request → call service → return response

#### 4b — CatalogService (generic or per-catalog)

- Extract the repeated CRUD+reorder pattern from `GameStatusController`, `GamePlatformsController`, `GamePlayWithController`, `GamePlayedStatusController`, `GameReplayTypesController`
- Option A: Generic `ICatalogService<TEntity, TResponse>` for shared CRUD
- Option B: Separate thin services per catalog entity
- Either way, controllers shrink to ~30-50 lines each

#### 4c — GameViewService

- Extract from `GameViewsController`: create, update, duplicate, reorder, configuration validation
- Move `ViewConfiguration` validation logic into the service

#### 4d — GameImportExportService

- Extract from `DataExportController`: `ExportFullDatabase`, `ImportFullDatabase`, `SelectiveExportGames`, `SelectiveImportGames`, `UpdateImageUrls`, `AnalyzeFolders`, `AnalyzeDuplicateGames`
- This is the biggest extraction — the 1,350-line controller will shrink dramatically

#### 4e — UserService

- Extract from `UsersController`: user CRUD, role checks, seed data
- Centralize `SeedUserDefaultDataAsync` (currently duplicated in `Program.cs` and `UsersController`)

**Risks:**

- Medium — business logic changes if not careful
- Must preserve exact request/response shapes
- Must preserve query behavior (filtering, sorting, pagination)

**How to verify:** Test every endpoint with the frontend. Compare API responses before/after. Run any tests.

**Frontend changes needed:** None — API contract stays the same.

---

### Phase 5 — DTO/Contract Standardization

**Goal:** Rename DTOs to follow the request/response convention. Use records where appropriate.

**Files affected:**

- All DTO files (renamed)
- All mapping extensions (updated)
- All controllers and services (updated references)
- Frontend TypeScript types (may need updating)

**Actions:**

- Rename `GameDto` → `GameResponse`
- Rename `GameCreateDto` → `CreateGameRequest`
- Rename `GameUpdateDto` → `UpdateGameRequest`
- Rename all catalog DTOs similarly
- Convert simple DTOs to records
- Move inline DTOs from controllers to `Contracts/` folder
- Split multi-DTO files into domain-grouped files

**Risks:** Low — pure renaming.

**How to verify:** Build succeeds. Frontend TypeScript types match (update if in same repo).

**Frontend changes needed:** Update TypeScript types to match new names (no API route changes).

---

### Phase 6 — Error Handling and Validation

**Goal:** Standardize error responses and validation.

**Files affected:**

- `ErrorHandlingMiddleware.cs` (standardize to English)
- Service methods (add consistent business validation)
- Request DTOs (add validation attributes)

**Actions:**

- Translate Spanish error messages to English
- Add `[Required]`, `[MaxLength]`, `[Range]` attributes to request DTOs
- Move business validation from controllers to services
- Ensure consistent ProblemDetails-style responses
- Remove `X-User-Id` header fallback from `UserContextMiddleware` in production

**Risks:** Low — error messages change but status codes stay the same.

**How to verify:** Test error cases (duplicate names, missing fields, invalid IDs).

**Frontend changes needed:** Update error message handling if frontend parses error text.

---

### Phase 7 — Code Style and Comment Cleanup

**Goal:** Make the code look human-written, clean, and professional.

**Files affected:** All files.

**Actions:**

- Remove unnecessary comments identified in Section 7
- Translate remaining Spanish comments to English
- Remove XML doc comments that add no value
- Remove section divider comments (`// ─── private helpers ────`)
- Remove `// Configure X entity` comments from DbContext configs
- Improve variable/method names where unclear
- Ensure consistent formatting
- Remove commented-out code

**Risks:** None — cosmetic changes only.

**How to verify:** Code review. Build succeeds.

**Frontend changes needed:** None.

---

### Phase 8 — Security Hardening

**Goal:** Fix security concerns.

**Files affected:**

- `appsettings.json` (remove default JWT secret)
- `UserContextMiddleware.cs` (restrict `X-User-Id` to development)
- `Program.cs` / extension methods (enforce env-var JWT secret)
- Controllers missing `[Authorize]`

**Actions:**

- Remove default JWT secret from `appsettings.json` — require environment variable
- Add environment check to `X-User-Id` header fallback
- Audit all controllers for missing `[Authorize]` — add where needed
- Verify `BackupScheduleController.RunNow` doesn't capture `HttpContext` after request ends

**Risks:** Low — may break local dev if env var not set. Document the requirement.

**How to verify:** App starts with env var. App rejects requests without auth token.

**Frontend changes needed:** None.

---

### Phase 9 — Tests

**Goal:** Add tests for critical paths.

**Files affected:**

- New test project: `GamesDatabase.Tests/`

**Actions:**

- Create test project with xUnit, FluentAssertions, Moq (or NSubstitute)
- Test `GameService` — CRUD, filtering, sorting, pagination
- Test `AuthService` — login, token generation, password hashing
- Test `GameImportExportService` — CSV parse, import merge logic
- Test `GameHistoryService` — entry creation, max-entries pruning
- Test `CatalogService` — CRUD, reorder, special status logic
- Integration tests for key endpoints using `WebApplicationFactory`

**Risks:** None — additive only.

**How to verify:** All tests pass.

**Frontend changes needed:** None.

---

### Phase 10 — Final Verification

**Goal:** Confirm everything works end-to-end.

**Actions:**

- Full frontend smoke test
- Test Docker build
- Test CasaOS deployment
- Review folder structure matches target
- Review `Program.cs` is under 50 lines
- Review all controllers are thin
- Review no direct DbContext in controllers (except simple reads if justified)
- Final code style review

---

## 10. Detailed Implementation Checklist

### Phase 0

- [x] Verify app builds and runs
- [x] Test login, game CRUD, export via frontend
- [x] Back up SQLite database
- [x] Commit baseline

### Phase 1

- [x] Create `Configuration/ServiceCollectionExtensions.cs`
- [x] Extract `AddOptionsConfiguration()` from `Program.cs`
- [x] Extract `AddPersistence()` from `Program.cs`
- [x] Extract `AddApplicationServices()` from `Program.cs`
- [x] Extract `AddCorsConfiguration()` from `Program.cs`
- [x] Extract `AddJwtAuthentication()` from `Program.cs`
- [x] Extract `AddApiDocumentation()` from `Program.cs`
- [x] Create `Configuration/WebApplicationExtensions.cs`
- [x] Extract `UseApiPipeline()` from `Program.cs`
- [x] Extract seeding into `DatabaseInitializer.cs`
- [x] Verify `Program.cs` is under 50 lines
- [x] Verify app starts identically

### Phase 2

- [x] Create `Domain/Entities/` and move entity files
- [x] Create `Domain/Enums/` and extract enums
- [x] Create `Infrastructure/Persistence/` and move DbContext
- [x] Create `Contracts/Requests/` and move/rename request DTOs
- [x] Create `Contracts/Responses/` and move/rename response DTOs
- [x] Create `Application/Mapping/` and move mapping extensions
- [x] Create `Application/Interfaces/` and move service interfaces
- [x] Create `Application/Services/` and move service implementations
- [x] Create `Common/` and move query helpers, date normalizer, etc.
- [x] Update all `using` statements
- [x] Verify build succeeds

### Phase 3

- [x] Create `Infrastructure/Persistence/Configurations/` folder
- [x] Extract `GameConfiguration.cs`
- [x] Extract `UserConfiguration.cs`
- [x] Extract `GameViewConfiguration.cs`
- [x] Extract `GameStatusConfiguration.cs`
- [x] Extract `GamePlatformConfiguration.cs`
- [x] Extract remaining entity configurations
- [x] Replace `OnModelCreating` with `ApplyConfigurationsFromAssembly`
- [x] Create `IHasTimestamps` interface
- [x] Simplify `UpdateTimestamps()` to use interface
- [x] Add migration to fix `IsManuallyCompleted` → `is_manually_completed`
- [x] Verify no pending model changes

### Phase 4

- [x] Create `IGameService` interface
- [x] Create `GameService` — extract from `GamesController`
- [x] Thin `GamesController` to delegate to `GameService`
- [x] Create `ICatalogService` or individual catalog service interfaces
- [x] Create catalog service(s) — extract from 5 catalog controllers
- [x] Thin catalog controllers
- [x] Create `IGameViewService` interface
- [x] Create `GameViewService` — extract from `GameViewsController`
- [x] Thin `GameViewsController`
- [x] Create `IGameImportExportService` interface
- [x] Create `GameImportExportService` — extract from `DataExportController`
- [x] Thin `DataExportController`
- [x] Create `IUserService` interface
- [x] Create `UserService` — extract from `UsersController`
- [x] Thin `UsersController`
- [x] Centralize seed data logic
- [x] Register all new services in DI
- [x] Verify all endpoints return identical responses

### Phase 5 — SKIPPED

> **Intentionally skipped** to preserve frontend compatibility. DTOs retain their original names (`GameDto`, `GameCreateDto`, `GameUpdateDto`, etc.) so that frontend TypeScript types remain valid without changes. The `Contracts/` folder was adopted but DTO class names were not renamed to request/response convention.

- [ ] ~~Rename `GameDto` → `GameResponse`~~ — skipped
- [ ] ~~Rename `GameCreateDto` → `CreateGameRequest`~~ — skipped
- [ ] ~~Rename `GameUpdateDto` → `UpdateGameRequest`~~ — skipped
- [ ] ~~Rename all catalog DTOs to request/response convention~~ — skipped
- [ ] ~~Convert simple DTOs to records~~ — skipped
- [x] Move inline DTOs from controllers to `Contracts/`
- [ ] ~~Update frontend TypeScript types if in same repo~~ — skipped
- [x] Verify build succeeds

### Phase 6

- [x] Translate Spanish error messages to English in `ErrorHandlingMiddleware`
- [x] Add validation attributes to request DTOs
- [x] Restrict `X-User-Id` to development environment
- [x] Verify error responses are consistent

### Phase 7

- [x] Remove unnecessary comments (see Section 7 list)
- [x] Translate remaining Spanish comments to English
- [x] Remove XML docs that add no value
- [x] Remove commented-out code
- [x] Review naming and simplify where needed

### Phase 8

- [x] Remove default JWT secret from `appsettings.json`
- [x] Audit all controllers for `[Authorize]`
- [x] Fix `BackupScheduleController.RunNow` Task.Run safety
- [x] Document required environment variables

### Phase 9

- [ ] Create `GamesDatabase.Tests` project
- [ ] Add `GameService` unit tests
- [ ] Add `AuthService` unit tests
- [ ] Add `GameImportExportService` tests
- [ ] Add `GameHistoryService` tests
- [ ] Add integration tests for key endpoints

### Phase 10

- [ ] Full frontend smoke test
- [ ] Docker build test
- [ ] Final code review
- [ ] Update README

---

## 11. Migration Strategy

### Current State

17 migrations exist covering schema evolution from multi-user support through Steam integration. Migrations are applied at startup via `context.Database.Migrate()`.

### Schema Repair

`EnsureCompatibilitySchema` in `Program.cs` runs raw SQL to add columns/tables that should already exist from migrations. This is a safety net for databases that may have been deployed before certain migrations were created.

**Recommendation:** Keep schema repair temporarily but log a deprecation warning. Remove it once all known deployments have been updated past the latest migration.

### Adding a New Migration

```bash
cd "K:\Programacion\Main\Games Database\GamesDatabase.Api"
dotnet ef migrations add MigrationName
dotnet ef database update
```

### Before Applying Migrations

1. Back up the SQLite database file (`gamesdatabase.db`)
2. Review the generated migration code — check for destructive operations
3. SQLite limitations: cannot drop or rename columns directly. EF Core handles this via table rebuild, but verify data preservation
4. Test the migration on a copy of the database first

### Pending Issue

The `IsManuallyCompleted` column name is PascalCase while all others are snake_case. A future migration should fix this:

```csharp
// Migration to rename IsManuallyCompleted to is_manually_completed
migrationBuilder.RenameColumn("IsManuallyCompleted", "game", "is_manually_completed");
```

Note: SQLite may require a table rebuild for this operation. Test carefully.

---

## 12. Frontend Compatibility Notes

### Frontend Structure

The React frontend has a service-per-domain pattern in `src/services/`:

- `GamesService.ts`, `GameStatusService.ts`, `GamePlatformService.ts`, etc.
- Centralized `apiRoutes.ts` defines all API routes

### API Routes Used

```typescript
apiRoutes = {
  games: { base: '/games', byId: (id) => `/games/${id}`, ... },
  gameStatus: { base: '/gamestatus', active: '/gamestatus/active', ... },
  gamePlatform: { base: '/gameplatforms', ... },
  gamePlayWith: { base: '/gameplaywith', ... },
  gamePlayedStatus: { base: '/gameplayedstatus', ... },
  gameViews: { base: '/gameviews', ... },
  gameReplays: { forGame: (gameId) => `/games/${gameId}/replays`, ... },
  gameReplayTypes: { base: '/gamereplaytypes', ... },
  gameHistory: { forGame: (gameId) => `/games/${gameId}/history`, ... },
  users: { base: '/users', login: '/users/login', ... },
  dataExport: { full: '/dataexport/full', ... },
  export: { zip: '/export/zip', ... },
  backupSchedule: { base: '/backupschedule', ... },
  steam: { profile: '/steam/profile', library: '/steam/library', ... },
  config: { networkSync: '/config/network-sync' },
}
```

### DTO Shapes

The frontend expects camelCase JSON (ASP.NET default). Key response shapes must be preserved:

- `GameDto` fields (id, statusId, name, grade, critic, score, steamAppId, etc.)
- `GamePlatformDto`, `GameStatusDto`, etc.
- `PagedResult<T>` (data, totalCount, page, pageSize, totalPages)
- `GameViewDto` with nested `ViewConfiguration`

### Breaking Changes to Avoid

- Do not change JSON property names (backend uses PascalCase C# → camelCase JSON)
- Do not change API route paths
- Do not remove response fields
- Adding new response fields is safe (frontend ignores unknown fields)
- Do not change pagination response shape

### Strategy

Rename backend DTOs (classes) without changing the JSON serialization output. C# class name `GameResponse` serializes identically to `GameDto` — only the source code name changes.

---

## 13. Testing Strategy

### Priority Tests

1. **GameService** (after extraction) — filtering, sorting, pagination, CRUD, score calculation
2. **AuthService** — login with/without password, token generation, password hashing
3. **GameImportExportService** — CSV parsing, import merge/upsert logic, selective export
4. **GameHistoryService** — entry creation, max-entries pruning
5. **CatalogService** — reorder logic, special status management, duplicate name detection

### Test Project Setup

```bash
dotnet new xunit -n GamesDatabase.Tests
dotnet add GamesDatabase.Tests reference GamesDatabase.Api
dotnet add GamesDatabase.Tests package Microsoft.EntityFrameworkCore.Sqlite
dotnet add GamesDatabase.Tests package FluentAssertions
dotnet add GamesDatabase.Tests package Moq
dotnet add GamesDatabase.Tests package Microsoft.AspNetCore.Mvc.Testing
```

### Testing with SQLite

```csharp
var options = new DbContextOptionsBuilder<GamesDbContext>()
    .UseSqlite("Data Source=:memory:")
    .Options;

using var context = new GamesDbContext(options);
context.Database.OpenConnection();
context.Database.EnsureCreated();
```

### What to Test First

1. Score calculation formula (`CalculateScore()`)
2. Game filtering with view filters
3. Import CSV parsing and merge logic
4. History entry creation with field diff detection
5. Catalog reorder logic

### What NOT to Test

- Simple CRUD that just calls `context.SaveChangesAsync()`
- Mapping extensions (unless they contain conditional logic)
- Configuration registration

---

## 14. Final Recommendation

### Do First (Highest Impact)

1. **Phase 1 — Clean up `Program.cs`** — fastest visual improvement, low risk
2. **Phase 4a — Extract `GameService`** — biggest controller (1,020 lines) becomes testable and thin
3. **Phase 4d — Extract `GameImportExportService`** — second biggest controller (1,350 lines) becomes manageable

### Do Not Skip

- **Phase 8 — Security hardening** — the default JWT secret and `X-User-Id` header are real risks

### Can Wait

- Phase 2 (folder reorganization) — cosmetic, can be done alongside other phases
- Phase 5 (DTO renaming) — pure naming change, low urgency
- Phase 9 (tests) — important but can come after services are extracted

### What Makes This Project Look Professional Quickly

1. A clean, 40-line `Program.cs` with named extension methods
2. Controllers under 100 lines each (delegate to services)
3. Consistent DTO naming (request/response pattern)
4. No unnecessary comments
5. All comments in English
6. Entity configurations extracted from DbContext
7. A test project with at least service unit tests
