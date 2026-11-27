# API deployment and recovery

[Español](DEPLOYMENT.es.md) · [API README](../README.md)

This is an operator checklist, not an executed deployment. Adopt the compatible
API before the Front. Electron is a separate distribution and is not covered here.

## Publication versus deployment

The API `main` workflow validates outgoing commit versions, restores audited
dependencies, builds/tests and publishes its amd64/arm64 image. Successful quality
and image jobs gate the immutable GitHub release for the exact tested HEAD.
Prepared `DOCS/releases/<version>.md` notes become its release body. Old tags,
releases and artifacts must not be moved or replaced under an existing version.

A push can publish new version and `latest` tags. Publication does not itself
authorize production startup, migration or restore. Inventory any external
automatic-update service before pushing; do not assume it is absent or configured.

## Preconditions

1. Record new/prior source SHAs, versions and image **digests**, target architecture,
   actual mounts/data paths and pending migrations. Verify native Skia decode/WebP
   in the final Linux image; Windows tests are not that smoke test.
2. Agree a practical backup/rollback method before startup. Quiesce writes and use
   SQLite's backup API, or an agreed stopped-service procedure with correct WAL
   handling. Never copy only a live main database and ignore its WAL/SHM state.
3. Hash the backup, check integrity/FKs and prove an isolated restore. Verify owned
   game/catalog/playlist/folder/view/Replay/history/preference data and a normal
   login without changing passwords. Preserve the previous image/config by digest.
4. CSV/ZIP is supplemental, not a complete SQLite/auth/history/cache backup. Keep
   databases, exports, signing keys and sessions out of repositories/web bundles.

## Compatibility and configuration

The runtime is .NET 10 LTS with Microsoft/EF 10, Swashbuckle 10 and SkiaSharp/Linux
native assets. The generated collection migration adds nullable explicit developer
and owned Home/GOTY preferences/revision. Onboarding adds owned state, a default-false
guide flag for existing users and sample provenance/artwork fields. Runtime/artwork
reads introduce no other migration. Check all older migrations actually pending too.
Review/receipt migrations add UTC view usage, additive-time mode, export-cache version
and retry-safe creation IDs. The post-3.0.0 `AddPinnedQuickViews` adds nullable
pin slots plus a unique owner/slot index; existing rows remain unpinned and otherwise
unchanged. It is not in the original 3.0.0 image. Duration/date review adds no migration.

Startup applies pending EF migrations automatically. Container defaults are
port 8080, database under `/app/data` and exports under `/app/exports`; these are not
evidence of an instance's actual paths. Configure the real `GAMESDATABASE_DB_PATH`,
JWT issuer/audience/private signing key, Steam keys, export/image/cache settings
and mounts intentionally. Never put credentials into a frontend API URL.
Only a genuinely empty database initializes the current model/migration baseline.
Persist DataProtection keys privately for signed metadata reviews; Testing uses
ephemeral protection and does not create deployed keys.

Critic review v2 and the scoped auth/media repairs need no additional migration.
v2 uses a different DataProtection purpose: an old API cannot accept an OpenCritic
plan as Metacritic on rollback. Review [the contract](METADATA_REVIEW.md) before
adopting a newer Front; expired/old reviews may simply need another lookup.

Keep `STEAM_FRONTEND_BASE_URL`, `STEAM_CALLBACK_BASE_URL` and exact permitted CORS
origins intentional. Callback state now binds frontend and `return_to`; unconfigured
or invalid destinations fail closed instead of trusting callback parameters. Auth
limits partition on the connection IP, not raw forwarding headers. Trusted proxy
configuration and real Steam/hosting compatibility still require operator verification.

ZIP media uses a separate credential-free transport with DNS/address/redirect,
time/byte and decoded-image limits. `ExportMedia:AllowedPrivateBaseUrls` can permit
an exact non-root administrator-owned private subtree. Configured `ImageSettings:BaseUrl`
and `NetworkSync:ImageBaseUrl` retain only their `/game-images/` subtrees; no loopback/
link-local/whole-origin exception or TLS bypass is implied. Verify the actual NAS
configuration before deployment; rejected image URLs remain in CSV/metadata.
Platform logos support verified raster and passive SVG, with an inert sandboxed
response. Unsafe legacy values are not rewritten; serving returns404.

Review bootstrap administrator, TLS, CORS/origins, headers, rate/load limits and
network exposure before broader access. Start the API, confirm migration/health/
version and owned reads, then adopt the matching Front. Verify images, draft
Save/Cancel, Focus/shelves, Home conflicts and readable exports without altering
data just to match expected counts. Player sync/date actions remain explicit.

An API restart loses in-memory sync-operation status, not saved games. Starting
another sync requires an explicit action; no startup/recurring sync is implied.
Verify exact date-save receipts and a stable saved finish after later sync. Bulk SQLite
contention returns503 `databaseBusy`/`Retry-After`; retain an import's successful receipt
and retry only unfinished settings instead of duplicating the import.

## Rollback

Prefer a rehearsed prior compatible image with additive columns/data retained.
The collection Down migration drops metadata/preferences; it is not a preserving
rollback after people have used them. Never run it automatically.
The isolated review/receipt/pin Up/Down/Up tests preserve old rows/FKs/SQLite sequences;
they do not preserve values in the newer fields after dropping those fields. Prefer a
compatible prior binary with additive schema retained, and separately reconcile writes
before any actual database restoration.

If restoring the database is necessary, stop writes and agree which newer writes
would be lost or reconciled. Preserve them, restore only the agreed verified
backup, check integrity/FKs and start the compatible image. Fix published versions
forward; no force-push or moved old tags to repair a release number.

## Español

Un push puede publicar imágenes de versión y `latest`; comprueba las actualizaciones
externas automáticas antes de subir. Publicar no autoriza desplegar, migrar o
restaurar. CI publica únicamente el HEAD probado, con notas humanas y tags
inmutables. Electron y el hosting real mantienen su validación aparte.

Antes de arrancar producción, registra SHAs/versiones/digests, arquitectura,
rutas/volúmenes reales y migraciones pendientes. Acuerda una copia SQLite consistente
con WAL, pausa escrituras cuando corresponda y prueba la restauración aislada con
integridad/FKs, datos propios, orden y login. Conserva la imagen/configuración
anterior. CSV/ZIP no sustituye una copia completa y las claves/sesiones nunca son
artefactos web o de Git.

La API usa .NET 10 y la migración de colección añade desarrollador nullable y
preferencias de Inicio/GOTY. La guía añade estado propio y procedencia de ejemplos.
Solo una base vacía inicializa el modelo y registra su baseline; conserva las claves
DataProtection en una ubicación privada. El arranque aplica las migraciones pendientes:
revisa la lista del destino, no presupongas el mismo esquema. Verifica la imagen Linux
y la conversión nativa. Configura datos, firma JWT, Steam, exports/imágenes/cachés,
bootstrap, TLS/CORS/headers/límites/exposición para el entorno real.

Actualiza la API, comprueba salud/versión/lecturas y después el Front. No sincronices
Steam ni inventes datos para ajustar los conteos de aceptación. Reiniciar pierde
el estado temporal de una operación, no sus juegos ya guardados.

Prefiere volver a una imagen compatible manteniendo el esquema aditivo. Down
elimina datos nuevos y no es una restauración preservadora. Si hay que restaurar
SQLite, acuerda cómo conservar/reconciliar escrituras posteriores, utiliza solo
la copia verificada y comprueba de nuevo. No sobrescribas cambios recientes ni
renumeres tags publicados; corrige hacia delante.
