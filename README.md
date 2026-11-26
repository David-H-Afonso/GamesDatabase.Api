# Games Database API

**The owned data and integration layer behind Games Database.**

An ASP.NET Core/.NET 10 API for personal game libraries: games, catalogs,
playlists, playthroughs, statistics, Steam, imports and recovery. Each account
owns its collection; clients do not choose another user's identity.

[Español](README.es.md) · [Web application](https://github.com/David-H-Afonso/GamesDatabase.Front) ·
[Get started](DOCS/GETTING_STARTED.md) · [Architecture](DOCS/ARCHITECTURE.md)

## Capabilities

- JWT authentication, administrator/user management and owned catalogs/data.
- Game CRUD, sparse updates, literal Unicode name search, filters and saved views.
- Manual/automatic playlists, folders, capacity and atomic item/order operations.
- Revisioned Home/GOTY preferences and whole-collection statistics.
- Steam base/DLC achievement sets, preserved completion and explicit sync jobs.
- Independent artwork candidates and bounded SkiaSharp/WebP image thumbnails.
- Steam-first/Wikidata-second metadata previews with signed, expiring review plans.
- Optional onboarding, opt-in sample batches and timestamp-reviewed cleanup.
- Owned random discovery and stable daily recommendations at the UTC boundary.
- CSV/JSON/ZIP import/export and backup/network controls.

## Requirements and quick start

- **.NET 10 SDK**; Microsoft/EF packages 10.0.15, Swashbuckle 10.3.1, SkiaSharp 4.157.0.
- SQLite is supplied by EF Core. No separate database server is required.
- Node 22+ is needed only for repository version/release tooling, not the API runtime.

```sh
git clone https://github.com/David-H-Afonso/GamesDatabase.Api.git
cd GamesDatabase.Api
dotnet restore GamesDatabase.Api.sln
dotnet build GamesDatabase.Api.sln --configuration Release
dotnet test tests/GamesDatabase.Api.Tests/GamesDatabase.Api.Tests.csproj --configuration Release
```

Before running, configure a private signing key and an explicitly intended data
path. Follow [Get started](DOCS/GETTING_STARTED.md): existing data needs an agreed
consistent backup before startup, because pending migrations are applied.

```sh
dotnet run --project GamesDatabase.Api.csproj
```

Development profiles use HTTP on port 5011/HTTPS on port 7245 and expose `/swagger`.
Containers listen on port 8080. `/health` reports the running version.

## First installation and recovery

A genuinely empty SQLite database is initialized from the current EF model and
records the current migration baseline. A database with existing tables/history
is never treated as a fresh install: its pending migrations remain authoritative.
The first administrator and catalogs are seeded; example games are **not**.

The initial administrator has no password for local bootstrap. Set its password
before wider access. The guide is optional for newly created users and can be
reactivated without adding samples or changing their collection.

For an existing instance, retain its previous image by digest, take a WAL-safe
SQLite backup and test an isolated restoration before adoption. Migration Down
can remove new metadata and is not an automatic data-preserving rollback.
See [Deployment and recovery](DOCS/DEPLOYMENT.md).

## Configuration

| Name | Purpose |
| --- | --- |
| `GAMESDATABASE_DB_PATH` | Intended private SQLite file |
| `JWT_SECRET_KEY` | Strong private server signing key |
| `JWT_ISSUER` / `JWT_AUDIENCE` | Token issuer and audience |
| `JWT_EXPIRATION_MINUTES` / `JWT_REFRESH_TOKEN_EXPIRATION_DAYS` | Token lifetimes |
| `ExportSettings__DefaultExportPath` | Private export directory |
| `CorsSettings__AllowedOrigins__0` | Explicit browser origin |
| `ASPNETCORE_URLS` | Listening addresses/ports |
| `STEAM_API_KEY` | Server-only key for Steam player integrations |
| `HOUSEHOLD_REDIRECT_URIS` | Required exact callback allowlist, comma-separated; never a wildcard |
| `HOUSEHOLD_CLIENT_ID` | Client identifier when overriding the built-in Household client |

Never expose signing/Steam/network credentials to frontend variables. Local
`.env*`, Development settings, databases and exports do not belong in source or
container build contexts. Review actual TLS, origins, headers and exposure for
the deployment, not just Docker defaults.

The callback allowlist is validated on startup, including standalone installations.
Without Household, reserve a non-routable `.invalid` callback as explained in
[Get started](DOCS/GETTING_STARTED.md), rather than broadening redirect permissions.

## API contracts

- [Frontend API reference](DOCS/FRONTEND_API_DOCUMENTATION.md)
- [Collection, Home, statistics and discovery](DOCS/COLLECTION_EXPERIENCE.md)
- [Onboarding and reviewed metadata](DOCS/GUIDED_LIBRARY.md)
- [Steam achievement sets](DOCS/STEAM_ACHIEVEMENT_SETS.md)
- [Runtime/image compatibility](DOCS/RUNTIME_NET10.md)
- [Household authorization](DOCS/HOUSEHOLD_INTEGRATION.md)

Home and GOTY share per-user revisions; conflicts return 409. Positive manual time
replaces Steam by default or adds to it with `manualPlaytimeIsAdditive=true`.
Zero/empty manual time uses Steam; raw sources remain separate. Completion policy is distinct from estimated
story/completionist hours. Replays preserve dated records, not invented sessions.

Random choice supports the full owned library or an exact displayed-ID pool.
View usage stores UTC `lastUsedAt` without editing its configuration. Optional
`creationRequestId` UUIDs make manual row retries idempotent while the record exists.

Metadata lookup does not save or sync a player. Apply accepts only reviewed
fields and rejects a stale game timestamp. Steam Store is a public, availability-
dependent endpoint; Wikidata supplies CC0 facts. Neither supplies verified
story/completionist durations, and missing data is never used to clear a field.

## Verification

```sh
dotnet restore GamesDatabase.Api.sln -p:NuGetAuditMode=all -p:NuGetAuditLevel=moderate -warnaserror:NU1902,NU1903,NU1904
dotnet build GamesDatabase.Api.sln --configuration Release --no-restore
dotnet test tests/GamesDatabase.Api.Tests/GamesDatabase.Api.Tests.csproj --configuration Release --no-build
node --test scripts/versioning/core.test.mjs scripts/versioning/release.test.mjs
node scripts/versioning/cli.mjs check
```

Tests use isolated `Testing` databases and synthetic provider responses; they
never require personal SQLite, sessions or a versioned `.env.test`. Windows tests
do not substitute for a Linux-image/native-encoder smoke test.

## Release and contribution

Front and API version independently, with a paired 3.0 product release. The
[version kit](scripts/versioning/README.md) checks prepared versions against each
commit; an explicit `Release-As: 3.0.0` records the owner-selected milestone.
Publication is gated on checks and the exact tested HEAD. Publishing is not deployment.

See [Contributing](CONTRIBUTING.md) for checks, migration review and safe reports.
Open reproducible API issues in [GitHub Issues](https://github.com/David-H-Afonso/GamesDatabase.Api/issues).
Sensitive disclosures follow [Security](SECURITY.md), not public issues.

## License and scope

The API retains its [GPL-3.0 license](LICENSE.md), also copied into the container.
SkiaSharp retains its MIT license; the application license does not replace
third-party notices. This release covers web/API, not Electron or a certification
of an unreviewed production hosting/recovery configuration.
