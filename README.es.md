# Games Database API

**La capa de datos propios e integraciones de Games Database.**

API ASP.NET Core/.NET 10 para bibliotecas personales: juegos, catálogos,
playlists, partidas, estadísticas, Steam, importaciones y recuperación. Cada
cuenta tiene su colección; el cliente no elige la identidad de otro usuario.

[English](README.md) · [Aplicación web](https://github.com/David-H-Afonso/GamesDatabase.Front) ·
[Primeros pasos](DOCS/GETTING_STARTED.es.md) · [Arquitectura](DOCS/ARCHITECTURE.es.md)

## Funciones

- Autenticación JWT, administración de usuarios y datos/catálogos propios.
- CRUD, actualización parcial, búsqueda Unicode literal, filtros y vistas.
- Playlists manuales/automáticas, carpetas, capacidad y orden atómico.
- Preferencias Inicio/GOTY con revisión y estadísticas de toda la colección.
- Conjuntos de logros Steam base/DLC, completado conservado y sincronización explícita.
- Candidatos de imágenes independientes y thumbnails SkiaSharp/WebP limitados.
- Metadatos Steam primero/Wikidata después, con planes de revisión firmados y caducables.
- Guía opcional, ejemplos aceptados y borrado revisado por timestamp.
- Juego aleatorio propio y recomendaciones estables con cambio diario UTC.
- CSV/JSON/ZIP, backups y controles de sincronización de red.

## Requisitos e inicio rápido

- **SDK .NET 10**; Microsoft/EF 10.0.15, Swashbuckle 10.3.1 y SkiaSharp 4.157.0.
- EF Core incluye SQLite; no hace falta otro servidor de base de datos.
- Node 22+ solo para herramientas de versión/release, no para ejecutar la API.

```sh
git clone https://github.com/David-H-Afonso/GamesDatabase.Api.git
cd GamesDatabase.Api
dotnet restore GamesDatabase.Api.sln
dotnet build GamesDatabase.Api.sln --configuration Release
dotnet test tests/GamesDatabase.Api.Tests/GamesDatabase.Api.Tests.csproj --configuration Release
```

Antes de arrancar, configura una clave de firma privada y la ruta de datos
explícitamente deseada. Sigue [Primeros pasos](DOCS/GETTING_STARTED.es.md): una
base existente requiere copia consistente antes del arranque y sus migraciones.

```sh
dotnet run --project GamesDatabase.Api.csproj
```

Los perfiles de desarrollo usan HTTP en el puerto 5011/HTTPS en el 7245 y `/swagger`.
El contenedor escucha en el puerto 8080. `/health` informa de la versión ejecutada.

## Instalación nueva y recuperación

Una SQLite realmente vacía se inicializa desde el modelo EF actual y registra
su baseline de migraciones. Una base con tablas/historial no se trata como nueva:
sus migraciones pendientes siguen siendo la referencia. Se crea el administrador
inicial y los catálogos, **no** juegos de ejemplo.

El administrador inicial no tiene contraseña para el bootstrap local. Establécela
antes de ampliar acceso. La guía es opcional al crear usuarios y se puede reactivar
sin añadir ejemplos ni cambiar su colección.

En una instancia existente, conserva la imagen anterior por digest, crea una
copia SQLite consistente con WAL y prueba una restauración aislada antes de
actualizar. Down puede borrar metadatos nuevos y no es un rollback automático
que conserve datos. Consulta [Despliegue y recuperación](DOCS/DEPLOYMENT.md).

## Configuración

| Nombre | Uso |
| --- | --- |
| `GAMESDATABASE_DB_PATH` | Archivo SQLite privado elegido |
| `JWT_SECRET_KEY` | Clave privada y robusta de firma |
| `JWT_ISSUER` / `JWT_AUDIENCE` | Emisor y audiencia |
| `JWT_EXPIRATION_MINUTES` / `JWT_REFRESH_TOKEN_EXPIRATION_DAYS` | Duración de tokens |
| `ExportSettings__DefaultExportPath` | Directorio privado de exportación |
| `CorsSettings__AllowedOrigins__0` | Origen explícito del navegador |
| `ASPNETCORE_URLS` | Direcciones/puertos de escucha |
| `STEAM_API_KEY` | Clave de servidor para datos de jugador Steam |
| `HOUSEHOLD_REDIRECT_URIS` | Lista obligatoria de callbacks exactos separados por comas, sin comodines |
| `HOUSEHOLD_CLIENT_ID` | Identificador si se cambia el cliente Household incorporado |

No expongas claves JWT/Steam/red en variables del Front. `.env*`, configuración
Development, bases y exports locales quedan fuera del repositorio y del contexto
Docker. Revisa TLS, orígenes, cabeceras y exposición reales, no solo valores Docker.

El arranque valida los callbacks también en una instalación independiente. Sin
Household, reserva un callback `.invalid` no enrutable según
[Primeros pasos](DOCS/GETTING_STARTED.es.md), sin ampliar permisos de redirección.

## Contratos API

- [Referencia para el Front](DOCS/FRONTEND_API_DOCUMENTATION.md)
- [Colección, Inicio, estadísticas y descubrimiento](DOCS/COLLECTION_EXPERIENCE.md)
- [Guía y metadatos revisados](DOCS/GUIDED_LIBRARY.md)
- [Conjuntos de logros Steam](DOCS/STEAM_ACHIEVEMENT_SETS.md)
- [Runtime e imágenes](DOCS/RUNTIME_NET10.md)
- [Autorización Household](DOCS/HOUSEHOLD_INTEGRATION.md)

Inicio y GOTY comparten revisiones propias y devuelven 409 en conflicto. El tiempo
manual positivo sustituye a Steam o se suma con `manualPlaytimeIsAdditive=true`.
Cero/vacío recupera Steam; ambas fuentes siguen separadas. Completado no equivale a las horas
estimadas de historia/completista. Replays guarda registros, no sesiones inventadas.

El aleatorio acepta toda la biblioteca propia o IDs exactos mostrados. El uso de
vistas guarda `lastUsedAt` UTC sin editar su configuración. `creationRequestId`
opcional evita duplicar reintentos manuales mientras exista el registro asociado.

Consultar metadatos no guarda ni sincroniza jugadores. Aplicar acepta campos
revisados y rechaza un timestamp antiguo. Steam Store es público y depende de
su disponibilidad; Wikidata aporta datos CC0. Ninguno ofrece duraciones verificadas
de historia/completista; la falta de datos nunca borra un campo.

## Comprobación

```sh
dotnet restore GamesDatabase.Api.sln -p:NuGetAuditMode=all -p:NuGetAuditLevel=moderate -warnaserror:NU1902,NU1903,NU1904
dotnet build GamesDatabase.Api.sln --configuration Release --no-restore
dotnet test tests/GamesDatabase.Api.Tests/GamesDatabase.Api.Tests.csproj --configuration Release --no-build
node --test scripts/versioning/core.test.mjs scripts/versioning/release.test.mjs
node scripts/versioning/cli.mjs check
```

Los tests usan bases `Testing` aisladas y respuestas de proveedores ficticias.
No necesitan datos/sesiones personales ni `.env.test` versionado. Los tests Windows
no sustituyen la prueba de imagen Linux y codificación nativa.

## Publicación y contribución

Front y API versionan por separado con un hito de producto pareado 3.0. El
[kit de versiones](scripts/versioning/README.md) valida cada commit preparado;
`Release-As: 3.0.0` registra el hito elegido explícitamente. La publicación está
gated por comprobaciones del HEAD exacto. Publicar no es desplegar.

Consulta [Contribuir](CONTRIBUTING.es.md) para tests, migraciones y reportes seguros.
Los bugs reproducibles van a [Issues](https://github.com/David-H-Afonso/GamesDatabase.Api/issues).
Los reportes sensibles siguen [Seguridad](SECURITY.md), sin divulgación pública.

## Licencia y alcance

La API conserva su [GPL-3.0](LICENSE.md), también copiada al contenedor. SkiaSharp
mantiene su MIT; la licencia de aplicación no sustituye avisos de terceros.
La release cubre web/API, no Electron ni certifica un hosting/rollback sin revisar.
