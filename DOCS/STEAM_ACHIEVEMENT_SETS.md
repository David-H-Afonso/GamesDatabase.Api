# Steam achievement sets / Conjuntos de logros de Steam

## Completion policy

| Surface | Meaning |
| --- | --- |
| `fullCompletion=true` / Completed | Manual completion, a previously completed game, any completed public Steam set, or every current achievement unlocked. |
| `allAchievements=true` / 100% total | Every **current, public, obtainable** achievement from the base game **and all DLC**, including DLC the player cannot access. Archived achievements/sets, developer-only sets and deleted definitions do not block completion. |
| Manual non-Steam game | The manual 100% declaration qualifies for both filters. |
| Manual Steam game | Qualifies for Completed, but cannot override missing Steam achievements in 100% total. |

Both query parameters also support `false`, and automatic playlist rules use the
same names and policy. Completion filters can additionally narrow a saved view.
When both are true the query is an intersection; the frontend offers mutually
exclusive controls to avoid accidental stacking. No achievements is **not** 100%.

`game.completion` and `game.story` are estimated **hours**, not percentages.
Achievement completion never automatically changes the user's gameplay status,
finish date or manual declaration.

### Historical preservation

Migration `20261112095949_AddSteamAchievementSets` adds columns and a per-user
`steam_achievement_set` table. Before new definitions are synced, it backfills
`game.steam_completion_preserved` using the previous full-completion predicate:
at least one cached achievement for that user/game, and no locked achievement.
It does not rewrite manual completion or existing unlocks.

Every subsequent sync captures the previous full state before reconciling new
achievements. Historical completion is monotonic. A game can therefore remain
Completed with a current percentage below 100%, without falsely showing the 100%
badge. Steam's `is_completed`/`time_completed` are stored separately from ratios;
completed sets retain their trophy when later achievements are added.

For example, 197/243 achievements with four completed sets qualifies for Completed
but not 100% total. An old 42/42 game that receives a locked DLC remains Completed
but no longer qualifies for the strict current 100% filter.

## Public contracts

Authenticated, user-scoped routes:

- `GET /api/steam/achievements/{gameId}`: existing flat array, retained for clients.
- `GET /api/steam/achievements/{gameId}/progress`: grouped progress, HTTP 404 for
  missing/foreign games, HTTP 401 without authentication.

The new response contains:

```json
{
  "gameId": 1,
  "source": "sets",
  "lastSynced": "2026-11-12T10:00:00Z",
  "isCompleted": true,
  "isFullyCompleted": false,
  "completionPreserved": true,
  "totalAchievements": 243,
  "unlockedAchievements": 197,
  "eligibleAchievementsTotal": 243,
  "eligibleAchievementsUnlocked": 197,
  "sets": [],
  "achievements": []
}
```

The empty arrays above abbreviate the payload. Each set has `groupId`,
`displayName`, optional `dlcAppId`, `archived`, nullable/optional `isAchievable`,
`isCompleted`, optional `completedAt`, `totalAchievements`, `unlockedAchievements`.
An empty name for group 0 means the base game; clients localize that label.

Achievements retain the old fields and add `groupId`, `archived`, optional
`globalPercent`, `progressCurrent`, `progressMin`, `progressMax`. Individual locked
archived achievements are hidden; archived sets remain visible only when the
player earned an achievement in them. Unlocked archived entries retain history.

`totalAchievements` is the **strict, all-DLC** current denominator, not necessarily
Steam's player-specific denominator. `eligibleAchievements*` is the separate
count for sets Steam reports the player can access. Empty sets are not counted.

Game responses also expose readonly `isCompleted`, `isFullyCompleted`,
`steamCompletionPreserved`, `steamSetsCompleted` and `steamSetsTotal`, along with
the existing achievement counts. Library, playlist details, cards and Focus must
use these values instead of independently inferring completion from durations or
set counts.

Sync responses retain `success`, `gamesUpdated`, `achievementsUpdated` and add
`achievementsSyncSucceeded` and `warnings`. A playtime sync can succeed while the
achievement sync fails: this is reported as partial success, not silently shown
as a successful achievement update. `lastSynced` in the progress response denotes
the last **successful achievement** snapshot. `lastError` is an optional stable
error code, never an exception message or URL containing credentials.

## Steam adapter and reconciliation

The adapter reads `IPlayerService/GetGameAchievements/v1` and
`IPlayerService/GetUserAchievements/v1`. These methods work on the public host,
but their documentation may lag the client protocol, so keep them isolated.
Metadata text remains English, matching the previous integration.

- Cache the app/language schema for five minutes. Validate schema version/hash
  against the player snapshot and refresh/retry once on mismatch.
- Match `internal_key` to the schema, but retain `(user, app, ApiName)` identity
  for existing unlock records. Internal key and group ID zero are valid.
- Validate `x-eresult` even for HTTP 200. Explicit schema NoMatch (42) is distinct
  from a missing player payload, malformed response, privacy failure or timeout.
- Sparse unlock lists are expected; missing/invalid snapshots never clear saved
  unlocks, set trophies, metadata or the last successful sync time.
- User-triggered sync/import operations are serialized per user through database
  commit, preventing overlapping Focus/tab/bulk requests from overwriting history
  or inserting duplicate cache rows. Other users have independent gates.
- Retire definitions absent from a confirmed full schema using `is_active=false`;
  never bulk-delete unlock history. Exclude private/developer-only sets.
- Resolve icon filenames under Steam's achievement CDN before returning URLs.
- On unsupported methods (404/405/501), use the existing `ISteamUserStats`
  endpoints. Preserve already-known set metadata and report `setsUnavailable`.
- Sync remains on demand. The Steam tab can upgrade an unsynced legacy cache once
  when opened; Focus keeps its existing once-per-mount visible-games behavior.
  No recurring sync task, timer or worker is added.

The UI groups by set, distinguishes base/DLC, filters/searches without leaking
spoilers, provides explicit spoiler reveal, opt-in global rarity and partial
progress. Labels are available in English and Spanish.

## Backups, rollout and rollback

**Do not run the updated API/desktop against production before taking a consistent
SQLite backup.** Startup applies migrations automatically.

1. Agree the exact production database path and backup/rollback location before
   deployment. Use the user's Downloads/staging area for temporary artifacts.
2. Prefer SQLite's online backup API / `sqlite3 .backup` for a live database, or
   stop **all** GDB API/desktop instances and make a consistent stopped backup.
   Copying only the main file while a WAL database is live is not sufficient.
3. Keep the matching previous API/frontend binaries with that database snapshot.
4. Start the new API, then the matching frontend. Run one user-initiated Steam
   sync to populate sets for the library; no bulk sync is triggered at startup.
5. If rollback is needed, stop GDB and restore the complete pre-change snapshot
   and matching old binaries. Do not rely on `Down()` to preserve newly captured
   set metadata: it removes the new table/columns by design.

Full/selective CSV exports, scheduled CSV backups, ZIP and network-sync copies
carry `IsManuallyCompleted` and `SteamCompletionPreserved` as portable completion
metadata. CSV files missing these columns preserve existing flags. Imported false
history never erases a true historical completion. Cached set definitions can be
reacquired from Steam; a consistent database snapshot remains the complete backup.

## Verification

From the API repository:

```sh
dotnet build
dotnet test tests/GamesDatabase.Api.Tests/GamesDatabase.Api.Tests.csproj
```

From the frontend repository:

```sh
npm run build
npm run lint
npm run test
```

Tests cover the generated SQLite migration on synthetic legacy tables, preserved
100%, the 197/243 completed-sets case, all-DLC strict filtering, user isolation,
archiving, deleted definitions, empty/private/developer sets, schema mismatch,
HTTP-200 API errors, legacy fallback, CSV round trips, UI labels, spoilers,
keyboard operation and late/cancelled tab requests. None require a real database,
Steam key or user credentials.

## Resumen en español

- **Completados** reúne completados manuales, anteriores, por conjuntos y al 100%.
- **100% total** exige todos los logros actuales del juego y todos sus DLC,
  también los DLC sin acceso. Los archivados/imposibles no bloquean. En juegos
  sin Steam se respeta la declaración manual; en Steam no sustituye los logros.
- La migración preserva los antiguos 100% **antes** de sincronizar nuevos datos.
  Un error o un perfil privado no borra progreso ni trofeos guardados.
- Un Vampire Survivors con 197/243 y conjuntos completados entra en Completados,
  pero no muestra falsamente 100%. Los juegos aún al 100% conservan su insignia.
- Antes de arrancar la API nueva en producción, acordar ruta y copia SQLite
  coherente. Para revertir, detener la app y restaurar esa copia y los binarios
  anteriores. No copiar solo el archivo principal de una base WAL en uso.
- Tras actualizar ambos repos, sincronizar Steam una vez desde la interfaz.
  No se crea ninguna sincronización programada.
