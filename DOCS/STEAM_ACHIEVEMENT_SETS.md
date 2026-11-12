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
  No recurring sync is scheduled. Bulk jobs are enqueued only by an explicit POST;
  the worker waits idle otherwise and never restores/retries jobs at startup.

The UI groups by set, distinguishes base/DLC, filters/searches without leaking
spoilers, provides explicit spoiler reveal, opt-in global rarity and partial
progress. Labels are available in English and Spanish.

## Bulk sync operations (504 timeout fix)

Interactive clients must not hold one HTTP request open while checking a large
library. The new user-scoped contract is:

| Request | Response |
| --- | --- |
| `POST /api/steam/sync/jobs` | HTTP 202 + job snapshot and `Location` immediately, without waiting for Steam. Repeated starts while queued/running return the same job. |
| `GET /api/steam/sync/jobs/current` | `{ "job": ... }`, or an empty envelope when no job is known. Read-only; never starts work. |
| `GET /api/steam/sync/jobs/{id}` | Latest owned snapshot, or HTTP 404 for missing/foreign IDs. |

All routes require authentication. Status responses use `Cache-Control: no-store`.
Starting without a linked account is HTTP 400; a full queue is HTTP 429; shutdown
is HTTP 503. Competing single-game/legacy bulk sync, import/store-add, link changes
and disconnect return HTTP 409 while the same user has an active bulk job, instead
of waiting behind it until a proxy 504 or changing its account mid-operation.

A snapshot contains `id`, `state` (`queued`, `running`, `completed`, `failed`,
`interrupted`), UTC timestamps, `totalGames`, `processedGames`, `gamesUpdated`,
`achievementsUpdated`, `failedGames`, `warningCount`, optional `currentGameName`,
safe `error` code, and final `result`. `processedGames` includes attempted/skipped
games, while updated counters advance only after commit. A completed operation can
still have warnings; clients must not present that as an error-free update.

### Execution and preservation

- One FIFO worker, bounded to 16 queued jobs, plus the active job. This avoids
  flooding Steam. Only the latest status is retained per user; a failed/finished
  operation can be replaced only by another explicit start.
- The worker creates its own DI scope; its lifetime is not the POST's cancellation
  token or the browser connection. Closing the page does not cancel accepted work.
- Playtime is fetched once, games are re-read individually, and each game's changes
  are committed before reporting its progress. A late exception, process restart
  or duplicate AppID cannot roll back all earlier successful games.
- A failed game's pending EF changes are discarded before continuing; existing
  persisted completion is not reset. A Steam-account change stops the operation
  before committing a mismatched account snapshot.
- No entity/schema changes or additional migration are needed for this correction.
  The existing completion-preservation migration and criteria are unchanged.

### Browser behavior

`useSteamSyncJob` reads the current job when the Steam screen opens, without
starting a new one. It polls only queued/running operations, stops on completion
or unmount/account change, and bounds transient read retries to three. Every short
request has a 15-second deadline. Fetch deadlines abort the request, and timers/
abort listeners are cleaned up after completion to avoid accumulating resources.

An ambiguous start failure is reconciled with **GET current**, never an automatic
second POST. A failed status read retains the last counters and offers **Recover
status**. All provider/proxy errors are mapped to safe EN/ES messages rather than
displaying HTML, raw URLs or exception details. Committed progress invalidates the
game-list cache so returning to Home loads the new completion indicators.

### Lifetime and deployment limits

The queue/status are in memory for the existing single-API-instance deployment.
Returning to a page recovers status while that API process is alive. An API restart
interrupts work and loses the operation status, **not committed game progress**.
The UI reports the lost operation; the user may explicitly start a new library
pass. There is no unattended resume, recurring schedule or automatic work retry.

Deploy the API and frontend together (API first). The old `POST /api/steam/sync`
remains synchronous for legacy clients and can still exceed proxy timeouts; the
updated UI no longer calls it. There is no need to raise production proxy timeouts
to support the new job flow. Wait for old in-flight syncs and take the agreed
consistent backup before replacing production binaries; no production deployment
is performed by preparing these source changes.

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

Job tests additionally cover HTTP 202 before long work finishes, deduplication,
owner isolation, bounded FIFO queue, safe fatal failures, interruption, explicit
retry, per-game commit/cancellation, repeated AppIDs, account changes, browser
recovery, retry limits, timeout cleanup, and absence of idle/background sync starts.

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
- **Sincronizar todo** inicia ahora una operación con respuesta inmediata y
  progreso visible. Cada juego se guarda por separado; cerrar la pantalla no
  cancela lo solicitado. Al volver se recupera el estado sin duplicar la operación.
- Si la API se reinicia, el estado temporal de la operación se pierde, pero los
  juegos ya guardados se conservan. Se puede iniciar otra pasada explícitamente;
  no se reanuda ni se repite trabajo de forma desatendida.
