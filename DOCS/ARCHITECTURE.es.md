# Arquitectura de la API

Games Database usa una arquitectura por capas ASP.NET Core/.NET10 en un proyecto
de aplicación, con tests de integración independientes y EF SQLite.

```text
Controladores HTTP + autenticación
  Servicios de aplicación
    Consultas propias · reglas · proveedores públicos
      EF Core / configuraciones / migraciones
        SQLite
```

Los controladores delimitan HTTP, los servicios mantienen reglas y EF persiste.
Los DTOs no exponen entidades tracked. No se añade otro repositorio sobre EF:
propiedad, transacciones y consultas quedan visibles en cada servicio.

## Componentes

| Componente | Responsabilidad |
| --- | --- |
| `GameService`, `CatalogService`, `PlaylistService` | CRUD, filtros, reglas y orden propios |
| `CollectionService` | Inicio/GOTY con revisión y estadísticas completas |
| `GameDiscoveryService` | Aleatorio propio y rotación diaria UTC |
| `OnboardingService` | Guía explícita, procedencia y borrado de ejemplos |
| `GameMetadataService` | Planes firmados/caducables y Apply parcial con timestamp |
| `PublicGameMetadataProvider` | Lecturas públicas limitadas Steam/Wikidata |
| `DekuDealsDurationProvider` | HLTB y Metacritic/OpenCritic atribuidos desde HTML público acotado de Deku, horas originales y caché/cooldown |
| `Steam/*` | Auth, tienda, logros y sincronización explícita |
| `GameImportExportService`, `ZipExportService` | Formatos y remapeo de referencias |
| `ExportMediaDownloader`, `ExportMediaDestinationPolicy` | Media ZIP sin credenciales, acotada y con destinos verificados/fijados |
| `PlatformLogoContent` | Validación común SVG pasivo/raster para CRUD/import y respuesta aislada |
| `GamesDbContext`, configuraciones, migraciones | Esquema SQLite y transacciones |

## Consistencia y propiedad

La sesión determina el propietario; el body no elige otra cuenta. Operaciones
administrativas comprueban el rol actual. Inicio/GOTY comparten revisión.
Los planes de metadatos vinculan cuenta/juego/campos, caducan en10 minutos y
rechazan cambios posteriores al timestamp revisado.
Crítica v2 vincula proveedor/enlace/ámbito bajo otro propósito DataProtection;
v1 sigue siendo compatible. Confirmar coincidencias no inventa ediciones.
El PUT normal valida las relaciones antes de mutar campos tracked. El nonce Steam
vincula destino permitido y retorno firmado; la cuota auth usa identidad de conexión,
no cabeceras reenviadas arbitrarias.

Los ejemplos reciben un lote en el servidor. Borrar revisa IDs/timestamps de
juegos y listas en transacción. Se conservan por defecto los editados y siempre
las playlists mixtas. Nunca se borra por nombre.
Las vistas del tutorial son vistas normales retenidas y se crean solo al elegirlo;
limpiar ejemplos no las posee. Dos plazas de chincheta por cuenta son preferencias
independientes de filtros, orden manual, edición y uso reciente.

## Tiempo e integraciones

El manual positivo sustituye o se suma a Steam según elección explícita; cero/null
usa Steam. Las fuentes siguen separadas y el efectivo usa minutos de64 bits también
en filtros/orden SQL y estadísticas. Fechas/Replays no se convierten
en sesiones. Completado usa la política compartida manual/conservada/conjuntos/
logros actuales, no duración estimada.

Descubrimiento usa el día UTC del servidor y un orden propio estable que rota
diariamente, sin worker recurrente. Los metadatos son hechos revisados; Steam
playtime no se convierte en duración de historia/completista.
Las estimaciones opcionales de Deku Deals conservan las fracciones originales en
el plan firmado; se explica el redondeo a horas enteras antes de aplicar, sin migración.
La crítica prioriza Metacritic de la edición Steam y después Metacritic/OpenCritic
atribuidos en Deku. Reemplazar no elude bloqueos. No se guarda un porcentaje calculado
de usuarios como SteamDB; las notas anteriores se conservan. Los lotes explícitos del
navegador reutilizan rutas firmadas normales, sin programador de servidor.
Consulta [Revisión v2](METADATA_REVIEW.es.md).
Las fechas Steam se revisan aparte: eventos UTC válidos generan referencias de calendario
en la zona indicada, no sesiones reconstruidas ni finales de historia confirmados. Los
checks vinculan referencia/timestamp y los recibos detallan campos guardados. Las fechas
no se desplazan al volver a sincronizar. Consulta [el contrato de fechas](STEAM_DATES.es.md).

Los ajustes de importación reservan una transacción SQLite corta antes de leer, validan
catálogos propios y evitan ediciones ficticias ante no-ops. No hay transacciones durante
consultas públicas. Los bloqueos devuelven503 saneado, no400 de datos inválidos.

## Datos y operaciones

Una base vacía crea el modelo EF y registra su baseline. Bases con tablas/historial
usan migraciones pendientes reales. La compatibilidad complementa ese flujo y no
permite marcar como migrada una base existente.

Persiste SQLite, exports, caché/imágenes y claves DataProtection de forma deliberada.
Acuerda copia consistente y rollback probado antes de producción/migraciones.
Consulta [Despliegue y recuperación](DEPLOYMENT.md).

## Pruebas

El host `Testing` no carga env locales ni bases personales. Los tests HTTP usan
usuarios aislados y autorización/persistencia real; los mocks externos se
identifican como pruebas de parser/contrato. Hay regresiones de bootstrap vacío,
conflictos, propiedad, limpieza y planes de metadatos.

Consulta [Colección](COLLECTION_EXPERIENCE.md) y [Guía](GUIDED_LIBRARY.md).
