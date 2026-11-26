# Collection experience — API 3.0

These contracts use **.NET10 LTS**; see [runtime/image compatibility](RUNTIME_NET10.md).
They are additive to the existing game,
playlist, view, Replay, History, Steam and administration contracts. Transport
properties use camelCase. All routes below require a normal web bearer session and
derive ownership from that session; clients never supply another user's ID.

The single game editor also uses the authenticated, non-mutating
`GET /api/steam/app/{appId}/artwork` candidate route for independent Logo/Hero/Cover
draft previews. See [the integration guide](FRONTEND_API_DOCUMENTATION.md#november-2026-draft-artwork-candidates).
Steam/Replay/History contracts remain. [Guided library](GUIDED_LIBRARY.md) covers
onboarding, sample provenance, random/daily discovery and metadata preview/Apply.

## Literal name search

`GET /api/games` accepts `nameSearchJson`, a URL-encoded JSON document:

```json
{
  "version": 1,
  "includeTerms": ["metal gear"],
  "excludeTerms": ["survive"],
  "matchMode": "all",
  "matchType": "contains",
  "caseSensitive": false,
  "accentSensitive": false
}
```

Each term is a literal phrase, never SQL, a wildcard or a regular expression.
Inclusions combine with `any` or `all`; any matching exclusion rejects a result.
Empty inclusions impose no inclusion restriction. Names are normalized without
changing their stored/displayed value. The SQLite UDF `gdb_name_match` is registered
per connection; SQLite `LOWER`/`NOCASE` alone is not used for accent matching.

Limits: version 1, at most 20 terms in each group, 250 characters per term and a
bounded serialized request. Invalid JSON, unsupported options and invalid bounds
return 400. `searchMetadata=true` also searches assigned developer and comment;
the Library's ordinary search remains name-only. Legacy `search` remains accepted.

Saved views use `viewId` (or legacy `viewName`) and retain their configured filter
groups and sorting. An unknown/foreign view is an error, never a fallback to all
games. `maxStory` is an optional non-negative estimated main-story duration.

Automatic playlist rules accept the same `nameSearch` object. When present it takes
precedence over legacy `rules.search`. An untouched legacy rule is not rewritten
by reading/cancelling its editor. The guided editor represents its original
case/accent sensitivity when the owner explicitly saves it.

## Playlist preview and full results

`POST /api/playlists/preview` takes `{ rules, maxLength, previewCount }` and returns
the owned matching count, the count after rule/capacity limits and a bounded game
preview. It does not update a game, list, status, exclusion or capacity.

### Optional templates and rolling rules

Templates are client-side draft recipes, not automatically created server lists.
Existing playlist and Home-filter contracts add these optional rule fields:

| Field | Default / semantics |
| --- | --- |
| `playingNow` | `false`; match the owned status with semantic Playing role, independent of its name or Focus |
| `upcomingReleases` | `false`; require a valid stored release calendar date >= today's date, inclusive, without an upper horizon |
| `releaseTimeZone` | Supported timezone ID, required when `upcomingReleases=true`; clients supply the browser's IANA zone |

Example rules for unlimited upcoming releases:

```json
{
  "statusIds": [], "platformIds": [], "playedStatusIds": [],
  "upcomingReleases": true, "releaseTimeZone": "Europe/Madrid",
  "sortBy": "Released", "sortDescending": false,
  "orderedGameIds": [], "excludedGameIds": []
}
```

Keep playlist `maxLength=null` and rule `limit` unset unless chosen by the user.
Do not substitute today's literal date, a release-year filter or a 30/90-day
window. Every query resolves the current day in the saved timezone. Preview,
playlist reads, Home resolution and full results share the predicate. A stored
ISO date/timestamp's date part is a calendar date, not converted to UTC. Missing,
partial, invalid and past dates are excluded. There is no general `isReleased`
flag; Steam's `unreleased` notice/status labels are not used to invent one. Other
configured filters still combine with AND. An invalid/missing enabled-rule
timezone returns 400 on write/preview validation.

`playingNow` follows special-role reassignment and matches nothing if no owned
Playing role exists. Guided adds validate the proposed role and other rules before
changing status/history/exclusions. Favorites retains `favorite=true`.

The deterministic `gdb_release_on_or_after` SQLite function is registered per
connection and receives the resolved day as a query parameter. There is no schema
migration or recurring task. Automatic playlist reads are untracked read models;
their virtual members must not attach/persist through a later save.

Manual item ordering/removal continues to use **playlist item IDs**. Automatic
priorities/exclusions use **game IDs**. Moving a list between flat groups continues
to use `POST /api/playlists/{id}/move`; group organization is not an invented
create/update property. Existing playlist JSON transfers remain version 1 and do
not acquire folder organization implicitly.

## Configurable Home

| Route | Contract |
| --- | --- |
| `GET /api/home/config` | Own saved configuration or derived defaults; reading creates no preference record |
| `PUT /api/home/config` | Version 1, current revision and ordered rows; 409 on a stale concurrent revision |
| `POST /api/home/rows/resolve` | Read-only batch `{ rows }`; results indexed by stable row ID |
| `GET /api/home/rows/{id}/games` | Full saved source, independent of preview count; `page`, `pageSize`, optional literal `search` |

```json
{
  "version": 1,
  "revision": 0,
  "rows": [{
    "id": "my-playing-row",
    "enabled": true,
    "title": null,
    "source": { "kind": "playingNow" },
    "presentation": "covers",
    "previewCount": 12,
    "highlightFirst": true
  }]
}
```

There are at most 20 rows; IDs are stable bounded identifiers, preview count is
1–24, and presentation is `covers`, `cards` or `row`. Sources are `playingNow`,
`playlist` with an owned `playlistId`, `view` with an owned `viewId`, `filter` with
playlist-compatible `rules`, `recommendations` with included/excluded statuses,
or `summary`. No source accepts a URL or expression.

Resolved rows expose `rowId`, `state`, optional `sourceName`/`errorCode`, `items`,
`totalMatches`, `returnedCount`, `hasMore` and optional `summary`. Daily rows also
have `recommendationDay`, UTC `refreshAt` and `serverTime`. States are
`ready`, `empty`, `unavailable` or `error`. Empty/unavailable rows remain in the
configuration so the owner can repair them; they never become the whole library.
Removing/hiding a Home row does not delete its playlist, view or games. Playing
membership uses `SpecialStatusType.Playing`, not translated names or Focus.

Preferences share a per-user concurrency revision with personal GOTY. A 409 must
be shown and recovered by reloading, not silently retried as an overwrite.

## Statistics and personal choices

`GET /api/collection/statistics?year=2026&month=3&dateKind=finished&allRanking=false`
returns aggregates over the **entire owned collection**, not the current page.
`year` is 1900–2100, `month` is optional 1–12, and `dateKind` is `started` or
`finished`. The response contains totals, status distribution, twelve months,
individual dated game/Replay records, unique-game period ranking, achievement
unlocks by game, progress bins, near-complete games, accumulated playtime ranking
and explicitly assigned developer groups.

Each month also has nullable `artworkGame`: a real owned game selected from that
month's highest graded dated record, including Replays. It is not simply the most
recent record, an inferred developer, a fabricated session or monthly playtime.

`GET /api/collection/games` pages the games behind a metric. Supported `metric`
values: `all`, `completed`, `strict`, `closures`, `timed`, `missingTime`,
`missingFinish`, `missingStory`, `status`, `developer`, `progress`, `period`,
`started`, `goty`. Parameters include `year`, `month`, `value` (status/bin),
`developer`, literal `search`, `page`, `pageSize`. Search is applied before paging.

- Effective time uses Steam when manual minutes are null/zero. Positive manual
  minutes replace Steam unless `manualPlaytimeIsAdditive=true`, which adds both
  sources (missing Steam contributes zero only when positive manual time exists).
  Raw values remain separate. Manual input is 0–600,000 minutes; zero is normalized
  to null on explicit create/update/import, not backfilled into existing records.
  SQL filters/sorting and statistics use the same 64-bit arithmetic. Personal
  grade and Critic use /100; backend-calculated Score uses /10.
- Time is accumulated, not reconstructed into monthly sessions. Replays have no
  duration; stored start/finish dates are not play sessions.
- Broad completed and strict current 100% retain the existing shared completion
  policy. A custom Done status and an estimated completionist duration do not
  establish completion. `closures` means a valid stored game/Replay closing date.
- Achievement dates use stored required achievement unlocks in UTC. A completed
  individual set is not a historical timeline of whole-game 100% completion.
- Period records retain separate Replays. Ranking/average deduplicate by game and
  use the highest available grade in that period. Missing Replay grades do not
  inherit a game's grade.

`GET /api/collection/goty` returns `{ revision, selections }`, indexed by year.
`PUT /api/collection/goty/{year}` takes `{ revision, gameId }`; `null` removes an
explicit choice. Candidates must be owned and have a valid start/finish date on
the game or a Replay in that year. The choice is independent of rankings and
saved GOTY views. Conflicts return 409.

`developer` is a new optional game field (150 characters maximum). Existing games
start unassigned. CRUD/history support it; automatic assignment requires the
owner's reviewed public-metadata lookup, never guessing from a title.
Unrelated updates do not recalculate score; score remains backend-computed.

Artwork roles remain independent. Compatibility startup only copies legacy Cover
into Hero when it is actually adding a missing legacy Hero column; an intentionally
empty Hero in the modern schema must not be filled from vertical box art on every
restart. The corresponding SQLite startup regressions are in the API test suite.

## CSV, ZIP and backups

Full/selective CSV and game info JSON retain `Developer`. A legacy import with no
Developer header preserves existing metadata. An explicit empty field clears it;
selective custom overrides can also clear/assign it. No backfill is guessed.

They also retain raw `ManualPlaytimeMinutes`, raw Steam time and
`ManualPlaytimeIsAdditive`. A missing legacy additive-mode header preserves the
existing mode; a new record defaults false. Explicit blank/custom-cleared mode is
false. Non-empty invalid booleans or out-of-range manual minutes reject that game
before changing it. CSV mode values are `True`/`False`, case-insensitive on import.
ZIP cache metadata version 2 forces old game info documents to refresh without
pretending their game data was edited. Transfers do not flatten effective time
back into either raw source.

ZIP and network exports share database cache records, not destination files.
Network sync compares the actual destination `info.json` before skipping a game,
so a prior ZIP cannot consume its metadata upgrade or a later raw-time/mode edit.
Identical JSON is not rewritten; missing metadata is regenerated. This adds a
metadata read per unchanged network game. Clearing export flags is bookkeeping:
it does not advance game/view/playlist edit timestamps or invalidate a reviewed
metadata plan. Real business edits still update timestamps and dirty flags.

Full exports and scheduled/manual CSV backups add a versioned
`Type=CollectionPreferences` record. Its `CollectionPreferencesJson` carries Home,
GOTY and owner-scoped named references. Full import resolves references against
the destination user's own rows; missing/ambiguous references reject the whole
preference document without overwriting existing preferences. Successful restore
increments the shared revision. Legacy imports without this record preserve
preferences. Selective game exports do not include another person's Home/GOTY.

ZIP and explicit network settings exports also include
`Settings/CollectionPreferences.json`; the CSV remains the supported import
entry point. Existing limits of old playlist/folder export formats are not a
substitute for a complete SQLite backup.

## Schema, isolation and rollout

Migration: `20261124165114_AddCollectionExperience` adds nullable `game.developer`
and the user-owned `collection_preferences` table. Onboarding adds
`20261125153304_AddOnboardingAndSamples`. Review generated Up/Down and verify
changes only on isolated copies before an existing-instance upgrade.

`20261126083645_AddLibraryReviewPreferences` adds UTC view usage, additive-time mode
and export metadata-cache version. `20261126091145_AddRetrySafeGameCreation` adds
nullable internal creation receipts and an owner/key unique index. The reviewed
native SQLite column-drop Down paths preserve the AUTOINCREMENT high-water mark;
the isolated Up/Down/Up test is a rehearsal, not a production restore authorization.

`LocalDevelopment:SkipEnvironmentFile=true` avoids importing an unrelated `.env`.
In Development, `LocalDevelopment:DisableScheduledBackups=true` suppresses the
background backup worker and redirects **manual** backups to the explicit local
export root. UNC/unspecified local test roots are rejected. Network sync remains
disabled; a local export URL points only to the loopback test API.

Before a future production startup, agree an exact consistent SQLite backup
(including WAL handling), integrity/hash and isolated restore test, and record the
previous image digest. Production startup automatically applies migrations: do
not deploy an image first and plan the backup afterwards. Prefer rolling back the
image while keeping additive columns/preferences, not dropping new data with
`Down`. A transactional Down is a rehearsal tool, not data preservation. Review
Linux native-image behavior, TLS/auth/origins and actual hosting separately.
Publishing a version is not deployment or authorization to restore user data.
