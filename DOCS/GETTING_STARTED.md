# Run the API locally

[Español](GETTING_STARTED.es.md)

Use the .NET 10 SDK and a compatible Games Database Front 3.0. The complete paired
walkthrough is in the [Front repository](https://github.com/David-H-Afonso/GamesDatabase.Front).

1. Restore/build/test the solution using the README commands.
2. Configure a strong private `JWT_SECRET_KEY`, deliberate `JWT_ISSUER` and
   `JWT_AUDIENCE`, an explicitly chosen `GAMESDATABASE_DB_PATH` and private
   `ExportSettings__DefaultExportPath`. Set `HOUSEHOLD_REDIRECT_URIS` to exact
   comma-separated callbacks. Startup requires this allowlist; without Household,
   reserve `https://household-client.invalid/callback` rather than a permissive URL.
   Override `HOUSEHOLD_CLIENT_ID` only if the client identifier also changes.
3. Choose a loopback listening URL and explicit frontend origin. Do not place
   secrets in the Front or commit `.env*`/Development configuration.
4. Start the API:

```sh
dotnet run --project GamesDatabase.Api.csproj --no-launch-profile --urls http://127.0.0.1:5011
```

5. Verify `/health` and version 3.0, then run the Front.
6. Sign in with the fresh bootstrap administrator and set its password before
   broader access. New accounts can opt into the guide; sample games require
   another explicit choice in that guide.

## Database initialization

For a genuinely empty SQLite file, app startup creates the current EF model and
records the current migration baseline. Historical migrations assume an original
schema, so direct EF database-update is not the fresh-install initialization path.

Existing tables/history are not treated as an empty install. Their real pending
migrations apply at startup. Before pointing a new image at such data, agree a
consistent WAL-safe backup and isolated restore. Do not run migration Down or
restore over newer changes automatically.

## Development tools

`dotnet tool restore` restores the repository's matching EF10 CLI for migration
development. Generate migrations with that tool, review Designer/Snapshot and
exercise them only on an isolated database. Build/test does not require EF CLI.

The test project uses `Testing`, skips environment-file loading and owns disposable
databases. Never point it at personal data or solve a fixture failure with real
credentials. See [Contributing](../CONTRIBUTING.md) and [Recovery](DEPLOYMENT.md).
