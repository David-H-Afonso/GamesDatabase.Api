# Security Review

## Account-owned backup hotfix — 27 November 2026

Scope: restore the visible backup tools to authenticated Standard accounts, without
granting server administration or access to another library. CSV import/export and
database duplicate inspection retain their existing owner filters. Image URL repair,
folder inspection, explicit orphan deletion and network sync use the authenticated
owner, not a supplied user ID. Standard cache clearing targets only that owner's
`_proxy_cache/{userId}`; global cache clearing and scheduled-backup administration
remain administrator operations. No migration or existing-data repair is included.

Before exposing network sync, its legacy unbounded artwork HTTP path was replaced
with the existing credential-free `ExportMediaDownloader`: checked DNS/socket egress,
TLS validation, image decoding and byte/time budgets apply to user backups too.
Local image reads are owner-rooted and raster-verified. Image URL HEAD probes use
the same protected media transport and a five-second deadline, not TLS bypass.
Missing files no longer replace a valid logo with an invented `logo.png` URL.
Per-account sync exclusion prevents two concurrent backups from overwriting files.
Folder deletion rejects traversal/linked roots; account cache enumeration skips links.
Errors no longer return the NAS authentication/path warning to ordinary clients.

`OwnedBackupAccessTests` covers real JWT/routes/CSV/SQLite and synthetic owned
filesystem sync, full and incremental exports, own image repair, foreign-account
preservation, cache/orphan scope, anonymous denial, URL validation and a real
loopback listener that receives no forbidden probe. External raster responses in
the successful sync test are explicit fixtures, not a live-NAS claim. Existing media
egress tests retain DNS/rebinding/redirect/credential checks. Test run results are
recorded separately; this block is not a full application security certification.

No secrets/environment files, production library, NAS service/configuration or
server runtime were read or modified for this repair. Publication does not deploy
the server. Earlier evidence below remains historical.

## Later 27 November — explicit cross-platform Deku search

This focused source repair supersedes the earlier485-test count below, not its
historical evidence. **Current complete API:492 passed,0 skipped; isolated Release
0 warnings/errors.** Five new provider regressions and two concrete-parser → real
Program/JWT/controller/signed-review/SQLite tests cover the changed discovery path.

The anonymous public search had implicitly inherited Deku's Switch/Switch2 default.
The provider now sends the documented public plural `platforms` preference for all
seven currently supported platform IDs in one bounded GET. Title input remains
one escaped query value and cannot inject a different scope. Scope-revised cache
keys reject older filtered misses/items; the public review version and shared
403/429 cooldown are unchanged. No arbitrary slug guessing, cookies, credentials,
redirects, alternative hosts, rate-limit bypass or direct HLTB call was added.

The actual product provider/UA reproduced both missing titles BEFORE, then read
the two exact public item URLs AFTER with4 GET200s. Scores/hours are attributed
public observations, not an independent platform review. The real API tests use
only deterministic upstream HTML and fresh synthetic SQLite, verify preview is
read-only and preserve unchecked fields/personal data/the unselected game on Apply.
Evidence: `Downloads/gdb-toolbar-deku-followup-20261127-v1/DEKU_PLATFORM_FIX_VERIFIED_20261127.json`.

No server, owner API binary/cache/data, environment file, schema, authentication,
authorization, normal token storage, CI, version, Git history or publication was
changed. This does not close the existing hosting/token/scanner boundaries below.
Frontend current source separately passes2338/202 tests, build/types/lint, native
contracts and v9 compiled Cypress69/69. Toolbar v8 proof seals284 exact before/after
pairs+34 actual resize/locale states,0toolbar findings; main v8 new-flow320 states
retain210 incompletes. A separate minimal v9 document-language attribute repair
has4 new unit regressions; its focused browser verification is tracked separately.
Older matrices remain labelled
v8/v7/v6 rather than promoted into a blanket final certificate. No frontend or
production-safety claim is inferred from this backend492-test result.

## Parent consolidation — pre-publication evidence,27 November2026

This snapshot records verification performed before the proposed publication and
supersedes the scoped test-only counts below without deleting their first-failure
history. **Source/integration verification, not production or final-SHA
certification.** No server deployment, data restoration, Git commit/push or remote
configuration change was performed during this verification. Later publication
must be checked against its actual approved commit/Actions separately.

- All five originally identified auth/ownership/media/rate-limit findings are
  integrated. `AddGamesDatabaseServices` registers `AddExportMediaDownloads`.
  The complete current API suite passes **485 tests, zero skips**, with a fresh
  Release solution build **zero warnings/errors** (`combined-api-artifacts-third`).
- New `ExportZipHostIntegrationTests` exercise actual Program/DI/JWT/controller/
  CSV/ZIP against isolated SQLite: anonymous401, dedicated client resolution,
  forged Host ignored and current-owner-only archives. This closes the missing
  media DI/combined-route proof noted below; no actual NAS is contacted.
- Unexpected ZIP/network exceptions no longer return messages/paths/stacks/type/
  inner exceptions to clients. `ExportErrorContractsTests` retained its initial
  3fail/1pass reproduction. Anonymous `/api/users/health` also returns only a stable
  unhealthy status/code; `HealthErrorContractsTests` retained its initial failure.
  Diagnostic exception details remain in server logs, not browser responses.
- Critic-v2 signed plans bind provider/URL/platform evidence under a separate
  DataProtection purpose. Actual changed-field receipts and no-op timestamps/history
  are checked. The Front validates exact game/fields/values/provider before reporting
  a successful Apply and never auto-retries an uncertain write. Metadata batches
  freeze owner/IDs, stop remaining work on navigation/account changes, and retry
  only information—not creation/import.
- `SteamCriticProvenanceTests` reproduced five false/invalid critic cases. New
  Steam imports/missing-critic sync no longer calculate a user-review percentage and
  call it SteamDB. Only a valid actual Steam Metacritic score is stored there;
  explicit attributed metadata can fill a missing rating. Existing Metacritic,
  OpenCritic and legacy SteamDB ratings are preserved. No existing data was relabelled.
- Fresh dependency reports: npm **0 vulnerabilities**; NuGet vulnerable/transitive
  report contains no vulnerable packages. No dependency versions/services were added.
  Installed native guards and versioning tests pass. GitHub Actions remain
  unpublished; no current dirty-source CI or image-runtime claim is made.

### Additional verification and boundaries

Three native browser journeys at390/768/1440, EN/ES, passed against actual API
Program/auth/controllers/signed plans/SQLite through a disposable Testing relay.
External Steam/catalog/Deku data and decorative CDN images were explicitly synthetic.
They prove manual+Steam creation, correct OpenCritic provider, preserved links and
unselected personal data, exact selected-ID replacement and no-op timestamp/history
retention without duplicate imports or metadata-triggered player sync.12 captures/
axe checks passed; no unexpected network, and owned5014 was released. Initial harness
content-root and selector failures remain separately retained. This is not a real
Steam/player/NAS test. A separate bounded current-parser public Deku smoke passed
for Hades and Hollow Knight; OpenCritic-only fallback is fixture-tested.

Main source review found no active-HTML/eval rendering path in the current Front;
the Household redirect is a server-validated authorization response. Parameterized
EF ownership and provider-name/URL validation are covered by the actual suites.
These observations are not a full SAST result. The attempted extra delegated
adjacent review did not execute successfully and provides **no audit credit**.

The dedicated egress policy is **ZIP-only**, not a blanket claim for configured
`NetworkSyncService` or `ImageProxyController` fallback. Those retain intentional
administrator-configured destinations, public decorative artwork access and legacy
operational behavior. Live destination/TLS/proxy/header/exposure checks are UNKNOWN.
No .env, private signing/session file, owner library or third-party player's data
was read for this block. Secrets/SAST/ZAP/React Doctor scanners and real deployment
settings were not certified; no large tooling or Docker/WSL substitute was installed.

Frontend current full suite passes2328/201 files with78.18% line coverage and
94.21% changed coverable lines; explicit20-file state/metadata safety draft has
91.56% branches. Broader35-file UI/recovery coverage is84.98% branches, and the
explicitly extended import/export37-file profile is84.70%; neither is silently
excluded or called a formal approved profile. Thresholds/exclusions are unchanged.
The new frozen v7 compiled Cypress passes69/69 with fail-closed synthetic fixtures;
critic/Create EN36+ES36 and batch EN66+ES66 capture/axe states pass. Named keyboard
scroll access and visible mobile Review above the batch footer were verified.
Main v7 synthetic browser evidence seals320 states across Chromium/Firefox/WebKit;
v7 entry34/34 and transfer116/116 selected states pass, with incompletes retained.
Broader v6 route/role evidence stays v6:324/328 reference cells captured,4 mobile
extended-Row entrypoints explicitly blocked. No full v7 UI certificate is inferred.
See the Front's private working checkpoint for current artifact/source identities.
The original Metro browser report, physical/assistive-device acceptance, Electron,
Linux image smoke, real hosting and final release/Actions remain separate boundaries.

### Parent evidence

Evidence root (local, not a deployed asset):
`C:\Users\Rikku\Downloads\gdb-critic-create-followup-20261126-v1\`.
Key logs: `combined-api-build-third.txt`, `combined-api-third.txt`,
`zip-host-first.txt`, `export-errors-red.txt`, `health-errors-red.txt`,
`steam-critic-red-second.txt`, `steam-critic-green.txt`, `npm-audit.json`,
`nuget-audit.json`, `critic-live-public.json`. Native evidence:
Front ignored `.tmp/3.0-preparation/critic-batch-native-v7-second/report.json`.
Fresh API recheck `api-v7-recheck.txt`/TRX again passes485 with0 skips against the
new isolated Release build0 warnings/errors. Initial native v7 CORS rejection is
retained at`critic-batch-native-v7-final`: the disposable relay omitted5191, not an
API security regression. Only its ignored exact-origin fixture/preflight changed;
the new host and second native run are separate and5014 was released after both.
All result counts above have their real logs/TRX; target-only reports below keep
their narrower original scope. No production backup/rollback was executed because
no production change was applied; operator prerequisites are in `DOCS/DEPLOYMENT*`.

## Project Detection

**Historical scoped auth report below:** the parent consolidation above owns the
current combined485-test/DI results. Original skipped/separate-owner statements
below describe that targeted run, not newly reopened combined-source findings.

26 November 2026, **current dirty source**, not a final-SHA or production-readiness certificate.
ASP.NET Core/.NET 10, EF Core/SQLite, JWT web sessions, Steam OpenID, xUnit and
`WebApplicationFactory`; the separate frontend is React/TypeScript/Vite/npm.
GitHub validation/publication workflows and Docker exist. This repair owns only
audit findings **1 (Steam callback), 2 (ordinary PUT ownership), 5 (auth limiter)**.
The parent review and a separate media repair own export/image findings 3–4.

## Executive Summary

The three scoped findings are **fixed in source and targeted tests**. Initial red
run: 30 failed / 8 passed. Final: **87 passed / 0 failed / 0 skipped**, comprising
43 security regressions and 44 related existing tests. Release solution build:
**0 warnings / 0 errors**. No full-suite, production, real Steam, browser, deployment,
dependency-scan or secret-scan certification is implied.

## Changes Made

- `Controllers/SteamAuthController.cs`, `Application/Services/Steam/SteamAuthService.cs`,
  `Application/Interfaces/ISteamAuthService.cs`: store the exact permitted frontend
  and OpenID `return_to` in single-use nonce state; validate required signed fields;
  never select callback/error redirects from an untrusted callback parameter.
- `Application/Services/GameService.cs`: preflight all four catalog relationships
  before any game/mapping/history mutation, including JSON type/positive-ID checks.
  Retain omitted/null semantics; normalize duplicate owned play-with IDs.
- Auth block only in `Configuration/ServiceCollectionExtensions.cs`: connection-IP
  partitions, normalized IPv4-mapped IPv6, no forwarded-header trust.
- New `SteamAuthSecurityTests.cs`, `GameUpdateOwnershipTests.cs`,
  `AuthRateLimitingTests.cs`; this report and scoped `AI_CONTEXT.md` notes.
- No schema, dependency, frontend, CI, version, runtime or deployment changes.
  Prior/concurrent dirty work is retained; shared-file changes outside these blocks
  belong to the parent/other work, not this repair.

## Findings

| Severity / finding | Status | Evidence and repaired boundary |
| --- | --- | --- |
| Critical / 1: attacker-selected Steam code redirect | **fixed (source/tests)** | `SteamAuthController.Callback` uses stored nonce destinations only; `SteamAuthService.ValidateCallbackAsync` requires exact stored `return_to`, signed identity/return fields, expected provider, and successful exact `is_valid:true` proof. Tampering fails before credentials or link writes. |
| Critical / 2: foreign catalog IDs through ordinary PUT | **fixed (source/tests)** | `GameService.UpdateGameAsync` preflights status/platform/played-status/play-with ownership before mutating tracked entities. HTTP and same-scope tracker tests prove whole-patch rejection without timestamps/history/pending changes. |
| High / 5: installation-wide auth quota | **fixed (source/tests)** | `AddGamesDatabaseRateLimiting` partitions `auth` by `Connection.RemoteIpAddress`, not attacker-controlled forwarding headers. A exhaustion does not exhaust B. |
| High / export/image findings 3–4 | **separate repair / not certified here** | Parent/media owner must provide current combined evidence. This report does not mark those findings fixed or re-open them based on an older snapshot. |

## Environment Variable Classification

Names/classification only; no environment files or deployed values were inspected.

| Name / location | Classification | Frontend-safe | Action |
| --- | --- | --- | --- |
| `JWT_SECRET_KEY` / backend configuration | Server secret | No | Unchanged; never copy into browser bundles/reports. |
| `STEAM_API_KEY` / backend configuration | Server secret | No | Unchanged, not needed by these no-network auth tests. |
| `STEAM_FRONTEND_BASE_URL` / backend configuration | Server redirect policy; public URL, not a credential | URL itself only | Keep an explicit trusted HTTP(S) base without credentials/query/fragment. |
| `STEAM_CALLBACK_BASE_URL` / backend configuration | Server URL configuration | URL itself only | Not a substitute for a trusted frontend destination. No request-derived fallback was added. |
| `CorsSettings:AllowedOrigins` / backend configuration | Server origin allowlist | Public origins only | Existing exact configured origins only; no blanket localhost/desktop/wildcard additions. |

## Test Matrix

| Area | Status / evidence |
| --- | --- |
| Callback destination/signature binding, link tampering, nonce/code reuse | **added**: `SteamAuthSecurityTests`; real controller/service/auth/persistence, external Steam backchannel/profile fixtures only. |
| Configured HTTPS and explicit localhost/127.0.0.1 redirects | **added**: valid login/exchange and link roundtrips; no broader allowlist. |
| Foreign/missing catalogs, malformed catalog values, atomic rejection | **added**: `GameUpdateOwnershipTests`; all four relations, mixed patches, unchanged HTTP snapshots and same-scope change trackers/history. |
| PUT unauthenticated/foreign-game guard and owned/null/omitted relations | **added**: real JWT/middleware/HTTP coverage, no auth exemption. |
| A/B quota isolation, forwarded-header spoofing, IPv4/IPv6/null identity | **added**: `AuthRateLimitingTests`; real pipeline via TestServer connection properties, not a mocked limiter. |
| Metadata apply, creation retry, bulk ownership, Household scope/auth | **existing / rerun**: the four related test classes included below. |
| Full API suite, other endpoints, invalid/expired normal web JWT matrix | **skipped**: parent requested targeted testing until combined source stabilizes. |
| Frontend 401/403/429/500 UX, hostile rendering/uploads/exports | **skipped / other owner**: no frontend/media changes in this repair. |
| SQL injection, password-reset token matrix, hosting/cookie/CORS/headers DAST | **not re-certified**; password reset is not an implemented feature in this scope. |

## Auth And Authorization Matrix

- Anonymous ordinary PUT: **401 tested**. Foreign game ID with a real other-user
  JWT: **404 tested**. Foreign catalog reference on an owned game: **400 tested**.
- Allowed owned updates still succeed; optional relation omission preserves and
  null clears; null required status retains the existing legacy no-change behavior.
- Steam callback: only the nonce's stored frontend/owner/mode may be used. Invalid
  or already-consumed nonce returns **400**, with no caller-selected redirect.
  Known nonce plus failed validation redirects an error only to its stored frontend.
- Existing nonce/code lifetimes (15/5 minutes) retained; wall-clock expiry was not
  time-travel tested. A restart loses process-local pending flows; start again.
- Admin API matrix, frontend route guards and account persistence were not changed
  or comprehensively re-audited. Existing passwordless bootstrap/browser-readable
  session limitations are not evidence of current production exposure.

## Password Reset Review

No reset-token flow added or changed. Existing authenticated password-change,
hashing/session-revocation behavior is outside this focused repair.

## Rate Limiting Review

- Login, web refresh and Steam code exchange retain **10 requests/minute**, now
  shared across those endpoints **per normalized connection IP**, not globally.
- Raw `X-Forwarded-For`, `X-Real-IP` and `Forwarded` cannot rotate that partition.
  Missing connection identity shares a fail-closed unknown-connection bucket.
- NAT clients / a reverse proxy may legitimately share one connection-IP bucket.
  Per-end-client behavior behind a proxy requires explicitly trusted, correctly
  configured forwarding middleware; no implicit trust was installed here.
- Metadata/integration policies and frontend 429 UX remain unchanged. The separate
  integration policy's capacity/partitioning is not certified by these auth tests.

## CORS, Cookies, CSRF, Headers

No cookie/CSRF/header/proxy configuration changes. Callback origins still use the
existing configured frontend and exact CORS origin list. Arbitrary Referer-based
discovery and arbitrary callback/error redirect destinations now fail closed.
Actual deployed CORS/TLS/CSP/HSTS/forwarding settings remain **UNKNOWN**.

## Scripts

No scripts or dependencies added. API security/dependency scanners not run;
frontend `security:deps`/`security:all` not run. Secrets/SAST/React/ZAP checks were
skipped rather than reported as passing.

## GitHub Actions And Free Alerts

No workflows, permissions, repository settings, alerts or remote runs changed.
Existing local YAML inspection is not verification of deployed GitHub settings.
SBOM/license/provenance remains a parent release/licensing hand-off.

## ZAP And Dynamic Scanning

No ZAP or external/owner traffic. Dynamic tests use only the Testing factory and
fresh synthetic SQLite data, never the owner's library, backups or session stores.
Public-provider HTTP is intercepted by deterministic test handlers.

## React Doctor

Not applicable to these backend edits; not run on the separate frontend.

## Manual Checks Still Required

1. Parent runs the full combined suite after media/metadata source stabilizes.
2. Verify actual Steam OpenID and proxy/desktop/localhost deployment compatibility
   later on an explicitly authorized test target; fixtures do not certify Steam.
3. Keep `STEAM_FRONTEND_BASE_URL` or an existing allowed Referer origin configured.
   An unallowlisted explicit destination now returns 400 instead of silently
   falling back. Unconfigured/invalid frontend destinations also return 400.
4. Existing contaminated cross-owner rows, if any, are **not automatically rewritten**.
   No owner DB was inspected; any data cleanup needs separate scoped approval.
5. Confirm media findings, session/bootstrap boundaries, deployed secrets/settings,
   HTTPS/proxy security and full release evidence in the parent report.

## Commands To Run

All recorded runs used `TEMP`/`TMP` pointing at the approved OpenCode temp directory,
`ASPNETCORE_ENVIRONMENT=Testing`, `DOTNET_ENVIRONMENT=Testing`, and artifacts at
`C:\Users\Rikku\Downloads\gdb-critic-create-followup-20261126-v1\auth-security-api-artifacts`.
No owner processes or ports were started/stopped/contacted.

```powershell
$artifacts = 'C:\Users\Rikku\Downloads\gdb-critic-create-followup-20261126-v1\auth-security-api-artifacts'
$filter = 'FullyQualifiedName~SteamAuthSecurityTests|FullyQualifiedName~GameUpdateOwnershipTests|FullyQualifiedName~AuthRateLimitingTests|FullyQualifiedName~GameMetadataTests|FullyQualifiedName~BulkUpdateConcurrencyTests|FullyQualifiedName~GameCreationRetryTests|FullyQualifiedName~HouseholdIntegrationTests'
dotnet build GamesDatabase.Api.sln --configuration Release --artifacts-path $artifacts --no-restore
dotnet test tests/GamesDatabase.Api.Tests/GamesDatabase.Api.Tests.csproj --configuration Release --artifacts-path $artifacts --no-build --no-restore --filter $filter --logger 'trx;LogFileName=auth-security-regressions-final.trx' --results-directory "$artifacts\results-final"
```

Recorded results: build exit **0**, final test exit **0** / **87 passed**, and scoped
`git diff --check` exit **0** (Git emitted only existing line-ending conversion notices).
First red run exit **1** / **30 failed, 8 passed** is retained, not overwritten, as
`auth-security-red.log` and `results-red/auth-security-red.trx` under that artifact root.
Intermediate 38/83-passing logs and final 87-passing log/TRX are retained separately.

## Continuation Notes For AI Agents

These are unpublished source repairs only; no final SHA, full-suite, deployment or
production-safe verdict. Preserve all prior/concurrent changes. No commit/push,
version bump, schema migration, deployment or owner data action is authorized by
this report. Extend/merge this scoped report with the media and parent evidence;
do not replace their work or promote source/test evidence into production proof.

## Media Repair — Findings 3/4 (26 November 2026)

This append-only section owns **ZIP media egress and platform-logo content only**.
It preserves the auth/ownership/rate-limiter review above. Evidence is against
unpublished dirty source, synthetic SQLite fixtures, and the isolated QA host;
it is **not a final-SHA, complete-suite, real-NAS or production verification**.

### Findings, severity and status

| Finding | Severity | Status / repaired boundary |
| --- | --- | --- |
| 3: ZIP forwards a bearer credential to stored artwork; request Host/forwarded headers choose the CSV destination; arbitrary unbounded responses enter archives | High / P1 | **Fixed in implementation and targeted tests; DI integration/combined-host smoke remains with parent.** `ZipExportService` calls `IGameImportExportService.ExportFullDatabaseAsync(authenticatedOwnerId)` directly. No CSV HTTP call, authorization forwarding or request-derived destination remains. All game/playlist ZIP image paths use the credential-free, address-checked, bounded raster downloader. |
| 4: declared SVG/data image types can execute as a top-level app-origin document; CSV bypasses validation | High / P1 | **Fixed in source/targeted tests and isolated browser proof.** CRUD, full CSV import and serving share `PlatformLogoContent`. Raster types are decoder-verified; SVG XML is bounded with DTD/entities disabled and active constructs rejected. Every served logo receives CSP sandbox/script blocking/no-sniff; legacy malformed or active data returns 404, never an unsafe document. Stored artwork/rows are not rewritten or deleted. |

### Files and behavior changed

- `Application/Services/ZipExportService.cs`: direct owner-scoped CSV generation,
  configured CSV delimiter, common safe downloader for logo/hero/cover and playlist
  assets; archive extensions come from verified image content, not URL suffixes.
  The existing public `BuildZipAsync` signature is retained, but its incoming
  authorization argument is intentionally unused. Existing pin/date/transfer edits
  were preserved.
- New `Application/Services/ExportMediaDownloader.cs`,
  `Common/ExportMediaDestinationPolicy.cs`, `Configuration/ExportMediaOptions.cs`
  and `Configuration/ExportMediaHttpClient.cs`: dedicated `export-media` transport.
  It strips Authorization, Proxy-Authorization and Cookie even if accidentally
  added as client defaults; cookies/default credentials/proxies/automatic redirects
  are disabled. Normal TLS certificate validation is retained.
- DNS is resolved inside `SocketsHttpHandler.ConnectCallback`. Every answer must
  pass the egress policy; the socket connects to the checked IP, not a second DNS
  lookup. Each manual redirect is checked again. Loopback, unspecified, link-local,
  multicast, reserved/documentation/transition targets and unapproved private
  networks are denied. HTTP/1.1 exact avoids cross-origin HTTP/2 coalescing.
- Network bounds: **16 MiB per image, 10 seconds across headers/redirects/body,
  three redirects, 128 MiB / two minutes per scoped downloader**. Streaming checks
  actual decompressed bytes, including absent/misleading Content-Length. Cancellation
  from the caller is propagated. Non-image, truncated, oversized or unavailable
  artwork is omitted and remains available as its unchanged URL in the backup.
- New `Common/VerifiedImageContent.cs`: existing SkiaSharp verifies the real raster
  format and a fully decodable first frame, bounded to 40M pixels and 16384 per
  dimension. Originals/animation are retained; active remote SVG is not archived.
- New `Common/PlatformLogoContent.cs`, updated `CatalogService.cs`, platform loop in
  `GameImportExportService.cs`, and `Controllers/GamePlatformLogoController.cs`:
  maximum 500,000 encoded characters / 375,000 decoded bytes; raster logos have
  a 16M-pixel budget. SVG requires well-formed SVG-namespace XML, depth <=64 and
  <=10,000 elements, no DTD/processing instructions/script/event handlers/foreign
  objects/animation mutations/external hrefs. Passive SVG, local fragment references,
  bundled presets, HTTP(S) references, standard base64/URL-encoded SVG and the old
  unmarked-base64 SVG representation remain supported. Unsupported active SVG is
  rejected, not silently sanitized or persisted over a legitimate logo.
- The serving query excludes over-limit legacy values before loading/decoding them.
  Returned data uses the verified media type and `Content-Security-Policy: sandbox;
  default-src 'none'; script-src 'none'; ...`, `X-Content-Type-Options: nosniff`,
  `Referrer-Policy: no-referrer` and `Cache-Control: no-store`. Sandbox has neither
  `allow-scripts` nor `allow-same-origin`. Anonymous decorative logo access remains
  intentional; unsafe stored redirect schemes are rejected.
- Added `ExportMediaSecurityTests.cs`, `PlatformLogoSecurityTests.cs`; updated the
  three existing ZIP constructor call sites in `LibraryReviewContractsTests.cs`,
  `PlaytimeTransferCacheTests.cs`, and `GameViewTransferTests.cs` to use the real CSV
  service. No dependencies, migrations, version, frontend, CI or owner data changes.

### Egress configuration / required parent integration

**Required DI call** inside `AddGamesDatabaseServices`:

```csharp
services.AddExportMediaDownloads(configuration);
```

`ServiceCollectionExtensions.cs` was deliberately not edited by the media owner.
The parent must add that registration, resolve `IZipExportService` in the combined
Testing host and run a ZIP-route smoke before closing integration verification.

`ExportMedia:AllowedPrivateBaseUrls` is an optional **server-side administrator
allowlist**, not browser config or a credential. Entries must be exact HTTP(S)
scheme/host/port **and a non-root path subtree**, for example a synthetic
`http://192.168.42.10:8080/game-images/`. A whole-origin exception is not accepted.
Existing `ImageSettings:BaseUrl` and `NetworkSync:ImageBaseUrl` automatically permit
only their `/game-images/` subtrees; no configuration file or actual value was read
or changed. Exceptions permit only RFC1918 / IPv6 ULA addresses, never loopback or
link-local, even if explicitly listed. The positive NAS regression proves the
configured fixture subtree works while another host/port/path and traversal fail.

These URLs/allowlists are public configuration in meaning, but enforcement belongs
on the server. No secret is needed by the media client and no browser variable was
added. Do not broaden the policy to fix a misconfigured arbitrary private URL.

### Media test matrix and results

| Check | Status / evidence |
| --- | --- |
| ZIP owner isolation, actual CSV generation, malicious Host/forwarded headers, absent outgoing auth/cookie/proxy credentials | **added / passed**: real ZIP + CSV + SQLite services; only remote image responses are deterministic handlers. |
| Literal loopback/link-local/private/mapped IPv6/reserved destinations and unsafe schemes | **added / passed**: no outbound-handler invocation and no artwork entry; original URL preserved. |
| Redirect to denied target, safe relative redirect and bounded redirect loop | **added / passed**; actual transport disables automatic redirects. |
| Explicit configured NAS allowlist and prohibited host/port/path/traversal | **added / passed** with synthetic NAS fixture; no real NAS contacted. |
| Real transport policy for DNS-resolved localhost | **added / passed**: DNS resolves but prohibited answers are rejected before socket connect; no listening target used. |
| Non-image/SVG/truncated bytes, HTML MIME, declared and streamed oversize | **added / passed**; streams stop at the byte limit and unsafe bytes never enter the ZIP. |
| Caller cancellation and body that stalls after headers | **added / passed**; a real ten-second body deadline is exercised. |
| Malicious SVG CRUD and full CSV import, legacy serve/MIME confusion/malformed/oversized XML | **added / passed** via actual JWT/HTTP/controller/service/storage paths. Invalid import rows do not replace an existing valid logo. |
| Legitimate SVG/base64/percent encoding/legacy encoding and PNG/presets | **added / passed**, including sandbox/no-sniff response checks. |
| Top-level browser execution marker / legitimate SVG display / opaque sandbox origin | **isolated manual automation / passed**, details below. |
| New media security test count | **66** cases: 37 export +29 platform-logo regressions. |
| Related existing tests | **24** rerun; total scoped pass **90 / 0 failed / 0 skipped**. |
| Full API suite, frontend, final-SHA, Linux native-image, live NAS, hosting/reverse-proxy/TLS | **skipped / not certified here**; full combined suite waits for all owners' source. |

The first targeted run was **85 passed /1 failed**, solely a new test expecting
flat CSV import counters instead of the existing `catalogs.platforms` response.
The assertion was corrected to the real contract; no API response change was made.
`targeted-first.log` and `results/media-security-targeted-first.trx` retain that run.
Subsequent 86-pass and final 90-pass evidence are separate files, not substituted
for the failed evidence. API build exited **0**, **0 warnings /0 errors**.

### Isolated browser proof

Artifacts are confined to
`C:\Users\Rikku\Downloads\gdb-critic-create-followup-20261126-v1\media-security-api-artifacts\browser-proof`.
An ephemeral **Playwright 1.58.2** installation launched its own headless Chrome
context; no shared MCP browser/profile/session was used. The disposable Kestrel
host bound only guarded/free **127.0.0.1:5017**, used in-memory SQLite/public
fixtures, and invoked the actual `GamePlatformLogoController` and MVC result
executor. It did not invoke API Program/startup, read environment files, start jobs,
load a real DB, or contact the owner ports or any remote server.

Two saved malicious SVG fixtures (script and event-handler marker only) returned
404 during top-level navigation and never wrote `gdb-media-marker` on the app
origin. Chrome replaces empty 404 documents with its error page; the harness
explicitly records that navigation rejection and separately asserts the actual
404 controller response, rather than pretending the payload was rendered. A
legitimate SVG rendered at top level (screenshot retained); CSP sandbox made even
instrumented localStorage access throw SecurityError. Browser requests outside
the isolated origin were blocked and counted: **0 unexpected requests**. No
production behavior is implied. The exact spawned host/browser were closed and
5017 was confirmed free afterwards.

The first harness attempt exposed its own missing empty configuration provider,
not an application issue; it was corrected. Chrome's empty-404 asynchronous error
navigation also required separate short-lived pages; no application assertion
was weakened. Final `browser-proof.json`, `browser-proof.log`,
`legitimate-svg-sandboxed.png` and host/build logs retain the successful proof.

### Media commands, skipped checks and follow-up

All API test hosts use Testing and synthetic data; `TEMP`/`TMP` were set to
`C:\Users\Rikku\AppData\Local\Temp\opencode`. No stage/commit/push, real DB,
backup/keys/auth-session files, owner5173/5011 traffic/process changes, SSH or
deployment was performed.

```powershell
$artifacts = 'C:\Users\Rikku\Downloads\gdb-critic-create-followup-20261126-v1\media-security-api-artifacts'
$filter = 'FullyQualifiedName~ExportMediaSecurityTests|FullyQualifiedName~PlatformLogoSecurityTests|FullyQualifiedName~LibraryReviewContractsTests|FullyQualifiedName~PlaytimeTransferCacheTests|FullyQualifiedName~GameViewTransferTests'
dotnet build GamesDatabase.Api.csproj --artifacts-path $artifacts --no-restore
dotnet test tests/GamesDatabase.Api.Tests/GamesDatabase.Api.Tests.csproj --artifacts-path $artifacts --no-restore --filter $filter --logger 'trx;LogFileName=media-security-targeted-final.trx' --results-directory "$artifacts\results"
# From the browser-proof artifact directory, not from either repository:
dotnet build MediaLogoProof.csproj --artifacts-path './artifacts' --no-restore
node browser-proof.cjs
```

- **Parent action:** wire DI, smoke the combined authenticated ZIP route and run
  the full API suite after concurrent metadata work stabilizes. Media-owner tests
  instantiate the ZIP service directly, so they do not prove that missing DI call.
- Confirm the actual deployment's configured image base/subtree and any required
  additional administrator-owned NAS prefix on an authorized fixture target. No
  blanket private-network, loopback, self-signed-TLS or authenticated-media exception
  was added. Authenticated/private arbitrary image endpoints may intentionally fail.
- Raster support depends on the existing Skia build. Formats unavailable to that
  decoder fail closed; this is not a new codec dependency. Source URL and CSV data
  remain intact. Top-level SVG active constructs/animation/external hrefs are not
  served; any owner-requested conversion/cleanup needs separate data approval.
- Bounds cover downloaded artwork, not all CSV/JSON/ZIP metadata. ZIP assembly is
  still in memory; extremely large owned libraries/metadata require a separate
  streaming-export/resource-quota design.
- The new egress client is deliberately ZIP-only. Adjacent `NetworkSyncService`
  and configured `ImageProxyController` HTTP fallback were not modified or certified
  by these scoped findings; do not describe the change as a global outbound policy.
- Security scripts/`security:all`, secrets/dependency/SAST scans, GitHub settings/
  alerts, ZAP and React Doctor remain with the parent review; none was added or
  claimed passing by the media owner. No paid services or repository dependency
  changes were introduced by the isolated browser harness.
