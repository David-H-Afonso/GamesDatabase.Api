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
| `Steam/*` | Auth, tienda, logros y sincronización explícita |
| `GameImportExportService`, `ZipExportService` | Formatos y remapeo de referencias |
| `GamesDbContext`, configuraciones, migraciones | Esquema SQLite y transacciones |

## Consistencia y propiedad

La sesión determina el propietario; el body no elige otra cuenta. Operaciones
administrativas comprueban el rol actual. Inicio/GOTY comparten revisión.
Los planes de metadatos vinculan cuenta/juego/campos, caducan en10 minutos y
rechazan cambios posteriores al timestamp revisado.

Los ejemplos reciben un lote en el servidor. Borrar revisa IDs/timestamps de
juegos y listas en transacción. Se conservan por defecto los editados y siempre
las playlists mixtas. Nunca se borra por nombre.

## Tiempo e integraciones

El manual positivo sustituye o se suma a Steam según elección explícita; cero/null
usa Steam. Las fuentes siguen separadas y el efectivo usa minutos de64 bits también
en filtros/orden SQL y estadísticas. Fechas/Replays no se convierten
en sesiones. Completado usa la política compartida manual/conservada/conjuntos/
logros actuales, no duración estimada.

Descubrimiento usa el día UTC del servidor y un orden propio estable que rota
diariamente, sin worker recurrente. Los metadatos son hechos revisados; Steam
playtime no se convierte en duración de historia/completista.

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
