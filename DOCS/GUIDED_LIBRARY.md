# Guided library contracts — API 3.0

All routes use the normal authenticated web session and its owner ID. Responses
with owned data are `no-store`; clients must not supply another user's identity.

## Onboarding

| Route | Purpose |
| --- | --- |
| `GET /api/onboarding` | Current guide state; never creates progress or samples |
| `POST /api/onboarding/start` | `{ revision, restart }`; explicit resume/restart |
| `PUT /api/onboarding` | `{ revision, step, enabled, complete }` |
| `POST /api/onboarding/samples` | `{ revision, confirm: true }`; idempotent active batch |
| `POST /api/onboarding/samples/keep` | Reviewed adoption of examples |
| `DELETE /api/onboarding/samples` | Reviewed exact-batch cleanup |

Steps: `welcome`, `library`, `organize`, `progress`, `home`, `discover`, `finish`.
Complete is valid only on `finish`. Progress revisions reject stale writes with 409.
New-user `tutorialEnabled` defaults true on create; existing migrated users remain
false. Admin editing can enable it; self-service start does not seed games.

State includes version/revision/enabled/step, sample games, sample-list summaries,
and counts. Batch provenance is assigned by the server, never by a game name or
client-supplied flag. Samples have no Steam App ID and use bundled role-specific art.

Keep/remove requires `confirm: true`, current revision, `expectedGames` and
`expectedPlaylists`, each `{ id, updatedAt }`. IDs and timestamps must match the
current batch before changing anything. `removeEditedSamples=false` retains edited
records by default. Mixed/real playlists are retained; remaining item order is
normalized. Cleanup/adoption clears batch provenance without touching other accounts.
CSV transfers preserve ordinary data/URLs but do not recreate a cleanup batch.

Migration `20261125153304_AddOnboardingAndSamples` adds the owned state table and
sample/guide fields; existing metadata starts unmarked. Down removes new fields
and is not a production-preserving rollback after users write them.

## Daily and random discovery

`GET /api/collection/random-game` samples uniformly over the complete owned
library. Empty libraries return 404 `emptyLibrary`; there is no foreign/filtered
fallback and no mutation.

`POST /api/collection/random-game` accepts `{ gameIds: [17, 25] }` for exactly the
displayed page. IDs must be positive, distinct, owned and at most 1,000. An empty
array returns 404 `emptySelection`; malformed scope 400 `invalidDisplayedScope`;
any missing/foreign/concurrently deleted member 409 `displayedScopeChanged`.
It never broadens the pool or silently chooses from a surviving subset. Only
the explicit empty codes are empty states, not every 404 from an obsolete route.

## Recent views and retry-safe manual creation

`POST /api/gameviews/{id}/use` records UTC `lastUsedAt` for an owned view and returns
the owned summaries in their manual order. It does not change configuration,
manual order, `updatedAt` or export-dirty flags. Clients choose the two most recent
public views, falling back to the first two, and retain manual order within that
pair so reusing either does not shuffle the buttons. Usage is not a view-filter edit.

`POST /api/games` optionally accepts a non-empty `creationRequestId` UUID. Its
owner-scoped unique key and canonical payload hash are stored internally with
the game. Repeating the same payload returns that game without another creation
or history write; reusing the key with changed fields returns 409
`creationRequestConflict`. Invalid rows do not consume the key. Creation/history
commit atomically. The internal receipt is not a public game field or CSV transfer
identity; the protection lasts while its associated record exists.

Migration `AddLibraryReviewPreferences` adds view usage, export metadata version
and additive-time preference. `AddRetrySafeGameCreation` adds nullable internal
receipt fields/index. Both were EF-generated, then their Down paths reviewed to
use supported native SQLite DROP COLUMN, avoiding a table rebuild that resets the
AUTOINCREMENT high-water mark. Production rollback still requires its own agreed
backup/restore plan and cannot discard newer writes.

## Daily recommendations

`POST /api/collection/recommendations/preview?count=6` takes
`{ includedStatusIds: [], excludedStatusIds: [] }`. Empty includes means regular
`SpecialStatusType.None`; explicit includes may select special states. Exclusions
win. IDs must be owned; count 1–24, status groups at most 100.

The result has `day`, `serverTime`, UTC `refreshAt`, `totalMatches` and `items`.
An owner-specific stable order rotates once per UTC day; small pools can repeat
games. No scheduled worker or stored shuffle is needed.

Home supports `source: { kind: "recommendations", recommendations: { ... } }`.
Resolved rows include day/deadline/server time. Full row results use the same pool
and daily order, independently of preview count. Existing saved Home layouts are
not overwritten to insert a new row.

## Reviewed metadata

| Route | Purpose |
| --- | --- |
| `POST /api/games/{id}/metadata/preview` | `{ overwrite, includeArtwork, steamAppId?, wikidataId? }` |
| `POST /api/games/{id}/metadata/apply` | `{ token, fields }` |

Preview checks ownership first. Linked Steam identity cannot be silently replaced.
Unlinked names use bounded Steam/Wikidata searches; ambiguous results need explicit
selection. Steam facts are preferred; Wikidata fills missing facts with per-field
source links. Public source failures keep stored fields unchanged.

Supported fields: `name`, `released`, `developer`, `critic`, `logo`, `hero`, `cover`.
Critic Apply pairs the score with Metacritic provider. Personal grade, comment,
status, dates of play, manual time, completion and App ID are not enrichment fields.
Neither source provides verified story/completionist estimates, so those are not
derived from Steam lifetime or average playtime.

Plans are encrypted/signed by DataProtection, owner/game/field bound, expire after
10 minutes and include the reviewed game timestamp. Apply accepts only selected
reviewed fields, rejects changed games with 409 and writes a sparse update in a transaction.
Invalid/expired/tampered plans return 400; cross-account plans 403; foreign games 404.
Preview is limited to 12 requests/minute per account. No player sync or persistent
Steam app-cache update is performed.

Steam Store is a public availability-dependent endpoint, not a guaranteed stable
Steamworks metadata API. Wikidata structured facts are CC0; precise ambiguous
dates and missing labels are not guessed. Provider reads use cancellation,
bounded response sizes and rate-limit cooldowns. DataProtection keys should be
persisted privately for a deployed instance; `Testing` uses ephemeral protection.

Sources: [Steamworks](https://partner.steamgames.com/doc/webapi/ISteamApps),
[Wikidata access](https://www.wikidata.org/wiki/Wikidata:Data_access),
[Steam application ID](https://www.wikidata.org/wiki/Property:P1733).
