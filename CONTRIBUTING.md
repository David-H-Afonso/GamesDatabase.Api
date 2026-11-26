# Contributing to the API

[Español](CONTRIBUTING.es.md) · [Setup](DOCS/GETTING_STARTED.md)

Use the existing layered structure: controllers delimit HTTP, services own rules,
DTOs delimit transport and EF Core handles persistence. Keep ownership and
transactions explicit; do not add redundant repository layers.

## Verify

```sh
dotnet restore GamesDatabase.Api.sln -p:NuGetAuditMode=all -warnaserror:NU1902,NU1903,NU1904
dotnet build GamesDatabase.Api.sln --configuration Release --no-restore
dotnet test tests/GamesDatabase.Api.Tests/GamesDatabase.Api.Tests.csproj --configuration Release --no-build
node --test scripts/versioning/core.test.mjs scripts/versioning/release.test.mjs
```

Tests use isolated `Testing` databases and skip environment-file loading. Do not
point a test at a personal/production database or use real provider credentials.
Label mocked external-provider tests honestly; they do not prove live availability.

## Schema and contract changes

```sh
dotnet tool restore
dotnet ef migrations add DescriptiveMigrationName
```

Use the matching EF10 CLI. Never handwrite a migration; include its generated
Designer/Snapshot. Review Up/Down and rehearse on a copy with integrity/FK checks.
Do not run migration Down or restore on production as an automatic rollback.

Fresh installations initialize the current model at app startup. Existing data
uses pending migrations, not an invented baseline. Agree a WAL-safe backup and
isolated restore before an existing-instance upgrade.

Update DTOs, services, frontend contracts, EN/ES content and docs together. Include
unauthenticated, normal/admin, user-A/user-B, null/bounds and stale-write cases.
Keep public errors generic and logs free of secrets. Preserve known zero values,
separate artwork roles and explicit player synchronization.

## Submission

Explain scope, approach, commands/results and recovery impact. Use Conventional
Commits and the [version kit](scripts/versioning/README.md); owner-selected product
milestones use an explicit `Release-As`, not fabricated breaking changes.
Published versions/tags are not edited by routine work.

Keep source tests, generated EF migrations and useful contract docs. Do not commit
env/Development secrets, databases, exports, caches, build output or private reports.
For sensitive findings, contact the maintainer privately instead of posting keys
or exploit data in public issues. Contributions retain the repository GPL license.
