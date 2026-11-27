# Attributed metadata review v2

[Español](METADATA_REVIEW.es.md) · [Guided contracts](GUIDED_LIBRARY.md)

Single-game review v2 itself needs no schema change. Persistent jobs described
below add `AddMetadataAutofillJobs`; adopt the compatible API and back up SQLite
before startup. These additions belong to current source, not every older release.

## Preview and apply

Both routes require the normal authenticated owner context and return `no-store`:

```http
POST /api/games/{gameId}/metadata/preview
Content-Type: application/json

{
  "overwrite": false,
  "includeArtwork": true,
  "includeDurations": true,
  "includeCritic": true,
  "reviewVersion": 2
}
```

Optional identities are `steamAppId`, `wikidataId` and `dekuDealsSlug`; validate and
retain deliberate choices across subsequent edition selection. A stored Steam App
ID cannot be changed through this endpoint. `reviewVersion` defaults to 1; group
flags default true. Preview is bounded to 12 requests/minute per account.
Optional `bestMatch: true` also accepts one unique cosmetic Steam/Wikidata title
match; exact matches stay preferred. Deku accepts unique cosmetic matches too.
Numbers and edition words remain significant; no first/fuzziest result is guessed.

The result includes `overwrite`, `reviewVersion`, `matches`, `notices`, `fields`
and an optional opaque `token`. Supported fields are `name`, `released`,
`developer`, `critic`, `logo`, `hero`, `cover`, `story`, `completion`.
Each proposal contains its reviewed `current`, `value`, `source` and public `url`.
Duration proposals retain exact `sourceHours` alongside positive integer-hour
`value` (nearest hour, halves upward, minimum one). Existing zero values are not
silently treated as empty.

Critic proposals add `criticProvider`, nullable `currentCriticProvider`, optional
`criticUrl`, and `criticScope`: `steam-edition` or `listed-platform`. The priority
is Steam-edition Metacritic, then Deku's attributed Metacritic, then its attributed
OpenCritic. A /10 user badge is not a critic rating. Provider/link pairs are validated.
The game stores the score plus actual provider; public links/scope are review evidence,
not a new persisted HTML/source document.

With `overwrite: true`, checked groups are searched even when populated; review v2
bypasses Deku's parsed 24-hour search/item cache, never its cooldowns or blocks.
Equal values can be returned with `unchanged: true`, including provider comparison.
Missing data never authorizes clearing a field.

```http
POST /api/games/{gameId}/metadata/apply
Content-Type: application/json

{ "token": "<opaque reviewed plan>", "fields": ["developer", "critic"] }
```

The signed/encrypted ten-minute plan binds owner, game, fields, provider evidence,
overwrite mode and the reviewed edit timestamp. Apply validates all selected
fields, ownership and timestamp inside a transaction, then writes a sparse patch.
It returns `{ game, appliedFields, unchangedFields }`. `appliedFields` acknowledges
the exact selection; `unchangedFields` is its verified no-op subset. A complete
no-op does not change `UpdatedAt`, history or export-dirty flags.

Invalid/expired/tampered reviews return 400, another account's plan 403, missing/
foreign game 404, and a changed game 409. Clients must validate matching game ID,
field receipts, saved values and critic provider before reporting success. An
uncertain transport result is not permission to blindly repeat a mutation.

## Compatibility, sources and automatic batches

- v1 retains the old Steam-Metacritic critic contract; the new server accepts v1
  plans. v2 uses a distinct DataProtection purpose. An old binary cannot decrypt a
  v2 OpenCritic plan and accidentally store it as Metacritic during rollback.
- Ordinary facts remain Steam-first/Wikidata-second. The bounded public Deku HTML
  provider returns critic data independently of whether HLTB estimates exist.
  It validates title/canonical URL/attribution, uses no cookies/redirects/keys,
  enforces 1MB/10s bounds and 403/429 cooldowns, and never contacts HLTB directly.
  Search sends the site's public plural `platforms` preference explicitly:
  `switch_2,switch,ps5,ps4,xbox_series,xbox_one,steam`. Omitting it limits anonymous
  discovery to Nintendo; singular `platform=all` does not expand search. The
  scope-specific cache revision does not reuse older filtered misses or parsed
  items, and does not change `reviewVersion` or bypass the shared cooldown.
  Missing Completionist remains missing rather than reusing Main + Extra.
- SteamDB automation is omitted under its [FAQ](https://steamdb.info/faq/); no bypass
  or calculated Steam user-review percentage is presented as a fetched critic score.
  New Steam store/library import and missing-critic sync retain only valid actual
  Steam Metacritic scores. Existing curated/legacy ratings are not rewritten.
- The durable batch below freezes up to 1,000 owned targets and uses these same
  signed preview/Apply operations sequentially. A unique active-user slot prevents
  concurrent batches for one account; source reads respect provider cooldowns.
  Missing optional data and identical ratings are not ambiguous identities.
- Post-import opt-in targets only confirmed created/linked receipts after their
  selected settings succeed. Failure/retry is metadata-only, never another import.
  Personal progress, notes, grades, play dates and Steam linkage are outside the
  enrichment plan. Artwork refresh uses one verified role, not clear-then-player-sync.

## Persistent, explicitly requested jobs

```http
POST /api/metadata-autofill/jobs
Content-Type: application/json

{
  "clientRequestId": "a49f71b2-6db0-4b9d-8a91-79956e92cbcd",
  "gameIds": [101, 102],
  "overwrite": true,
  "acceptListedPlatformCritic": true
}
```

Start returns 202 with the owned job. IDs must be positive, distinct and all owned;
invalid/mixed scope is rejected as a whole. Same owner/key/payload returns the
same job; changing that payload or starting another active scope returns 409.
At most 16 active jobs are queued globally. The boolean platform choice is explicit:
Replace's UI confirmation discloses it once, while empty-only defaults false.
It accepts a reliable matching edition's attributed rating, not a verified Steam
platform match. Steam-edition Metacritic still wins; actual providers are retained.

| Route | Purpose |
| --- | --- |
| `GET /api/metadata-autofill/jobs/current?includeItems=false` | Active/latest non-dismissed owned summary; JSON `null` if absent |
| `GET /api/metadata-autofill/jobs?skip=0&take=10` | Owned history summaries, including dismissed earlier results |
| `GET /api/metadata-autofill/jobs/{id}` | Retained per-item fields/notices/statuses |
| `POST /api/metadata-autofill/jobs/{id}/cancel` | Stop pending work without undoing committed changes |
| `POST /api/metadata-autofill/jobs/{id}/retry` | Explicitly retry failed/partial/cancelled metadata only |
| `POST /api/metadata-autofill/jobs/{id}/dismiss` | Hide an inactive banner, not delete history |
| `POST /api/metadata-autofill/jobs/{id}/accept-listed-critics` | One explicit acceptance for platform-only rating choices; genuine ambiguity remains |
| `POST /api/metadata-autofill/jobs/{id}/items/{gameId}/review` | Apply an explicit fresh `{token,fields}` review and resolve only that item atomically |

Every route requires the authenticated owner and `no-store`; foreign job IDs return
404. DTOs contain safe status codes, public matches and actual saved field names,
never auth credentials or plan tokens. Counts distinguish processed, updated,
unchanged, choices, partial, failed and cancelled; `completed` does not mean every
optional source supplied data. A review can coexist with fields already saved.

The worker starts no unsolicited scan. It consumes only persisted explicit work,
claims a two-minute process lease and checkpoints every item. The game mutation,
history and durable job receipt share one SQLite transaction. If a commit response
is uncertain, the ledger—not a repeated blind write—determines recovery. Closing
the browser/logging out stops only observation; API restart resumes pending work
after the previous lease expires. The server must be running to process work.

Provider Retry-After/cooldown pauses are persisted per job. Transient source notices
receive at most three read attempts per item; successful/unchanged fields are not
reapplied on explicit retry. A concurrent game edit is left for fresh review.
Manual review of one completed item preserves siblings and another item's live
lookup lease. Earlier batches remain recoverable when a new one starts. SQLite
backup includes this operational state; CSV/ZIP library transfers do not resume it.

Persist deployed DataProtection keys privately. Prefer a compatible binary rollback
with additive data retained; old plans may require a new lookup. These contracts do
not authorize a migration, remote deployment or restoring over newer writes.
