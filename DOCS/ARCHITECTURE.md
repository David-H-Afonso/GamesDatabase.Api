# API architecture

Games Database uses a pragmatic layered ASP.NET Core/.NET10 architecture in one
application project, with an independent integration-test project and EF SQLite.

```text
HTTP controllers + authentication
  Application services
    Owned queries · business rules · public provider adapters
      EF Core context / entity configurations / migrations
        SQLite
```

Controllers handle HTTP boundaries; services own rules; EF owns persistence.
Transport DTOs do not expose tracked entities. A repository abstraction is not
added over EF: ownership predicates, transactions and query behavior are kept
visible in their service.

## Main components

| Component | Responsibility |
| --- | --- |
| `GameService`, `CatalogService`, `PlaylistService` | Owned CRUD, filtering, rules and ordered data |
| `CollectionService` | Revisioned Home/GOTY and whole-collection statistics |
| `GameDiscoveryService` | Owned uniform random choice and UTC-day rotation |
| `OnboardingService` | Explicit guide progress, sample provenance and reviewed cleanup |
| `GameMetadataService` | Signed expiring previews and sparse, timestamp-checked Apply |
| `PublicGameMetadataProvider` | Steam-first/Wikidata-second bounded public reads |
| `DekuDealsDurationProvider` | Attributed HLTB and Metacritic/OpenCritic from bounded public Deku HTML, exact hours and cache/cooldown |
| `Steam/*` | Authentication, store data, achievement policy and explicit sync jobs |
| `GameImportExportService`, `ZipExportService` | Transfer formats and reference remapping |
| `ExportMediaDownloader`, `ExportMediaDestinationPolicy` | ZIP-only credential-free bounded media with checked/pinned destinations |
| `PlatformLogoContent` | Shared passive SVG/raster validation for CRUD/import and sandboxed serving |
| `GamesDbContext`, configurations, migrations | SQLite schema, relationships and transactions |

## Consistency and ownership

Session identity determines the owner; request bodies cannot choose another
account. Administrator operations are checked against the actual account role.
Home/GOTY share a concurrency revision. Metadata plans bind owner/game/fields,
expire after10 minutes and reject changes to the reviewed timestamp.
Critic v2 binds actual provider/link/scope under a distinct DataProtection purpose;
v1 remains compatible. Same-value confirmations return receipts without false edits.
Ordinary PUT preflights every catalog relationship before mutating tracked fields.
Steam callback nonce state binds the exact permitted destination and signed return URL;
auth quotas partition by connection identity, not arbitrary forwarded headers.

Samples have server-assigned batch provenance. Cleanup reviews exact game/list
IDs and timestamps in a transaction; edited examples are retained by default and
mixed playlists are preserved. Names are never used as deletion criteria.
Tutorial views are ordinary retained views, created only by an explicit choice;
cleanup never owns them. Two nullable owner-scoped pin slots are preference metadata,
independent of filters, manual order, edit timestamps and recent use.

## Time and integration rules

Positive manual minutes replace Steam or add to it when explicitly selected;
zero/null uses Steam. Raw sources remain separate and effective arithmetic uses
64-bit minutes, consistently in SQL filtering/sorting and statistics. Stored dates
and Replay records are not reconstructed sessions. Completion uses the shared
manual/preserved/set/current-unlock policy, not estimated duration.

Daily discovery uses a server UTC day and a stable owner-specific order with a
daily rotation. No recurring worker is needed. Public metadata is factual and
reviewed; unsupported durations are not synthesized from Steam playtime. Optional
Deku Deals estimates preserve public fractional source hours in the signed plan;
the integer-hour storage proposal is disclosed before Apply, without a migration.
Critic priority is Steam-edition Metacritic, then attributed Deku Metacritic/OpenCritic.
Fresh replacement does not bypass source blocks. A calculated Steam user percentage
is not newly stored as a SteamDB critic score; existing curated ratings remain intact.
Explicit browser batches reuse normal signed endpoints, not a server scheduler.
See [Metadata review v2](METADATA_REVIEW.md).
Steam date references are a separate explicit workflow: valid UTC event timestamps
become review-zone calendar references, never reconstructed sessions or verified story
completion. Date selections bind candidate+timestamp; receipts list exact saved fields.
Saved dates never move on later player sync. See [the date contract](STEAM_DATES.md).

Bulk import settings begin a short SQLite transaction before reading, validate owned
catalog IDs and avoid fake no-op edits. No transaction spans public provider requests.
Busy/locked writes return a sanitized503 rather than blaming input with400.

## Storage and operations

An empty database initializes the current EF model and records its migration
baseline. Existing tables/history use actual pending migrations. Schema
compatibility code is supplementary, not permission to stamp an existing DB.

Persist SQLite, exports, image/cache data and DataProtection keys intentionally.
Take a consistent backup before production startup/migrations and keep a tested
rollback image. [Deployment and recovery](DEPLOYMENT.md) defines that boundary.

## Tests

The `Testing` host never loads local env files or uses personal databases. HTTP
tests exercise real authorization and persistence with isolated users; external
provider fixtures are labelled as parser/contract tests. Fresh bootstrap,
concurrent cleanup, ownership and metadata plans have dedicated regressions.

See [Collection contracts](COLLECTION_EXPERIENCE.md) and
[Guided library contracts](GUIDED_LIBRARY.md).
