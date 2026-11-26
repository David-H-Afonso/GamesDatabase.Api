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
| `Steam/*` | Authentication, store data, achievement policy and explicit sync jobs |
| `GameImportExportService`, `ZipExportService` | Transfer formats and reference remapping |
| `GamesDbContext`, configurations, migrations | SQLite schema, relationships and transactions |

## Consistency and ownership

Session identity determines the owner; request bodies cannot choose another
account. Administrator operations are checked against the actual account role.
Home/GOTY share a concurrency revision. Metadata plans bind owner/game/fields,
expire after10 minutes and reject changes to the reviewed timestamp.

Samples have server-assigned batch provenance. Cleanup reviews exact game/list
IDs and timestamps in a transaction; edited examples are retained by default and
mixed playlists are preserved. Names are never used as deletion criteria.

## Time and integration rules

Positive manual minutes replace Steam or add to it when explicitly selected;
zero/null uses Steam. Raw sources remain separate and effective arithmetic uses
64-bit minutes, consistently in SQL filtering/sorting and statistics. Stored dates
and Replay records are not reconstructed sessions. Completion uses the shared
manual/preserved/set/current-unlock policy, not estimated duration.

Daily discovery uses a server UTC day and a stable owner-specific order with a
daily rotation. No recurring worker is needed. Public metadata is factual and
reviewed; unsupported durations are not synthesized from Steam playtime.

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
