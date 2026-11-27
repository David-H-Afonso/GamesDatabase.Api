# Attributed metadata review v2

[Español](METADATA_REVIEW.es.md) · [Guided contracts](GUIDED_LIBRARY.md)

No schema change is introduced by this capability. These additions belong to the
current source; the README distinguishes it from published releases.

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
- There is no metadata batch endpoint, server queue or scheduler. The Front's
  explicit in-memory batch freezes up to 1,000 selected owned targets and performs
  these same preview/apply operations sequentially. Reliable proposals may be
  accepted automatically, but fuzzy identities and Deku's platform-uncertain
  Metacritic proposals remain for manual review.
- Post-import opt-in targets only confirmed created/linked receipts after their
  selected settings succeed. Failure/retry is metadata-only, never another import.
  Personal progress, notes, grades, play dates and Steam linkage are outside the
  enrichment plan. Artwork refresh uses one verified role, not clear-then-player-sync.

Persist deployed DataProtection keys privately. Prefer a compatible binary rollback
with additive data retained; old plans may require a new lookup. These contracts do
not authorize a migration, remote deployment or restoring over newer writes.
