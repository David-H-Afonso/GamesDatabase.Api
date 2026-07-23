# Household Integration v1

Games Database implements the Household Connection Protocol with user consent and PKCE
S256. Household never receives the source password, and Games Database has no global
service credential or static subject-to-user mapping.

## Browser authorization

Open the hash-router route:

```text
/#/integrations/household/authorize
  ?client_id=household
  &redirect_uri=<exact registered callback>
  &state=<unguessable state>
  &code_challenge=<base64url SHA-256 verifier>
  &code_challenge_method=S256
  &scope=profile.read games.read games.status.write
```

The route preserves the complete request through normal or Steam login, shows the source
account and requested permissions, and supports approve/deny. It never receives or stores
an integration access or refresh token.

On approval the SPA sends the request to
`POST /api/integrations/household/v1/authorize` with the normal Games Database JWT. The
response contains only an API-validated redirect URL with a one-time code and unchanged
state. Denial returns the registered callback with `error=access_denied` and the same state.

## Backend endpoints

| Endpoint | Authentication | Purpose |
|---|---|---|
| `POST /api/integrations/household/v1/authorize` | Games Database web JWT | Approve/deny and issue a five-minute code |
| `POST /api/integrations/household/v1/token` | Public, rate limited | Exchange PKCE code or rotate refresh token |
| `POST /api/integrations/household/v1/revoke` | Token in body, rate limited | Idempotently revoke the identified connection |
| `GET /api/integrations/household/v1/me` | Integration access token | Read connection/account identity and scopes |
| `GET /api/games` | Web JWT or integration access token | Integration requires `games.read`; results and filters are source-user scoped |
| `GET /api/games/{id}` | Web JWT or integration access token | Integration requires `games.read`; source-user scoped |
| `GET /api/games/summary` | Web JWT or integration access token | Integration requires `games.read` |
| `GET /api/GameStatus/active` | Web JWT or integration access token | Integration requires `games.read`; source-user scoped |
| `PATCH /api/games/{id}/status` | Web JWT or integration access token | Integration requires `games.status.write` |

Integration access tokens are opaque and use a dedicated authentication scheme. They are
not Games Database web JWTs and are rejected by endpoints outside this allowlist, including
other game, admin, export, delete, bulk, import, scan, and Steam-management endpoints.

## Storage and rotation

The migration `AddHouseholdConnections` creates separate connection, authorization-code,
access-token, and refresh-token tables. Raw credentials are returned only during the
backend exchange. The database stores SHA-256 hashes.

- Authorization codes are random, expire after five minutes, bind client, redirect URI,
  scopes and S256 challenge, and set `consumed_at` on first successful exchange.
- Access tokens expire after 15 minutes by default.
- Refresh tokens expire after 30 days and rotate atomically in a token family.
- Reusing a replaced refresh token revokes that family and its access tokens, not web
  sessions or another user's connection.
- Revocation is idempotent and revokes only the connection identified by the supplied
  integration access or refresh token.

## Exact server configuration

```text
HOUSEHOLD_CLIENT_ID=household
HOUSEHOLD_REDIRECT_URIS=https://household.example/api/integrations/callback/provider,http://localhost:5019/integrations/callback/provider
HOUSEHOLD_ACCESS_TOKEN_MINUTES=15
HOUSEHOLD_REFRESH_TOKEN_DAYS=30
HOUSEHOLD_AUTHORIZATION_CODE_MINUTES=5
```

`HOUSEHOLD_REDIRECT_URIS` is a comma-separated exact allowlist. Schemes, hosts, ports,
paths, query strings, and trailing slashes must match exactly; wildcards are unsupported.
These values are registered client metadata, not credentials, and must remain server-side
to keep deployment policy centralized.

## Tests

Run from `GamesDatabase.Api`:

```powershell
dotnet test GamesDatabase.Api.sln
```

The WebApplicationFactory suite uses an isolated temporary SQLite database and covers
PKCE, exact redirects, code reuse, refresh rotation/family reuse, revoke, scopes, endpoint
allowlisting, and user A/B isolation.
