# Revisión de metadatos v2 con procedencia

[English](METADATA_REVIEW.md) · [Contratos de la guía](GUIDED_LIBRARY.md)

La revisión individual v2 no necesita migración. Los trabajos persistentes descritos
abajo añaden `AddMetadataAutofillJobs`: actualiza la API compatible y copia SQLite
antes del arranque. Pertenece al código actual, no a todas las versiones anteriores.

## Consulta y aplicación

Ambas rutas exigen la cuenta autenticada normal y responden `no-store`:

```http
POST /api/games/{gameId}/metadata/preview
Content-Type: application/json

{
  "overwrite": false,
  "includeArtwork": true,
  "includeDurations": true,
  "includeCritic": true,
  "reviewVersion": 2
}
```

Las identidades opcionales son `steamAppId`, `wikidataId`, `dekuDealsSlug` y `launchBoxId`.
Valídalas y conserva las elecciones deliberadas al seleccionar otra fuente/edición.
Esta ruta no cambia el App ID Steam guardado. `reviewVersion` vale 1 por defecto;
los grupos valen true. La consulta tiene un límite de 12/minuto por cuenta.
`bestMatch: true` opcional acepta también una única coincidencia cosmética en Steam/
Wikidata, priorizando la exacta. Deku admite coincidencias cosméticas únicas.
Los números y palabras de edición siguen contando; no se elige el primer resultado.
Las coincidencias incluyen `releaseYear` nullable y la lista `platforms`. No se
mezclan conflictos conocidos de año/plataforma. Los IDs Wikidata explícitos se leen
directamente, sin depender del orden de búsqueda; un alias completo publicado es
evidencia válida. Cambiar la edición principal descarta elecciones secundarias antiguas.

El resultado incluye `overwrite`, `reviewVersion`, `matches`, `notices`, `fields`
y `token` opaco opcional. Admite `name`, `released`, `developer`, `critic`, `logo`,
`hero`, `cover`, `story`, `completion`. Cada propuesta conserva `current`, `value`,
`source` y `url` pública revisadas. Las duraciones añaden `sourceHours` exactas y
`value` en horas enteras positivas (al entero más próximo, mitades hacia arriba,
mínimo una). Un cero existente no se trata silenciosamente como vacío.

La crítica añade `criticProvider`, `currentCriticProvider` nullable, `criticUrl`
opcional y `criticScope`: `steam-edition` o `listed-platform`. Se prioriza Metacritic
de la edición Steam, después Metacritic atribuido en Deku y después su OpenCritic.
El indicador /10 de usuarios no es crítica. Se valida la pareja proveedor/enlace.
El juego guarda nota y proveedor real; enlaces/ámbito son evidencia de la revisión,
no un nuevo documento HTML persistido.

`overwrite: true` busca los grupos marcados aunque estén rellenados. La v2 evita
la caché analizada de búsqueda/página de Deku, nunca sus bloqueos o
pausas. Puede devolver coincidencias con `unchanged: true`, comparando también el
proveedor. Un dato ausente nunca autoriza vaciar un campo.

```http
POST /api/games/{gameId}/metadata/apply
Content-Type: application/json

{ "token": "<plan revisado opaco>", "fields": ["developer", "critic"] }
```

El plan firmado/cifrado caduca en diez minutos y vincula cuenta, juego, campos,
procedencia, modo y timestamp revisado. Apply valida todos los campos, propietario
y timestamp en una transacción y guarda un patch parcial. Devuelve
`{ game, appliedFields, unchangedFields }`: la selección exacta confirmada y su
subconjunto sin cambio. Un no-op completo no cambia `UpdatedAt`, historial ni
indicadores de exportación.

Un plan inválido/caducado/manipulado devuelve 400; de otra cuenta, 403; juego
inexistente/ajeno, 404; juego cambiado, 409. El cliente debe verificar ID, recibos,
valores guardados y proveedor antes de anunciar éxito. Una respuesta perdida no
autoriza repetir una mutación a ciegas.

## Compatibilidad, fuentes y lotes automáticos

- v1 conserva la crítica Steam-Metacritic anterior y la API nueva acepta esos
  planes. v2 usa otro propósito DataProtection: una API antigua no puede descifrar
  un plan OpenCritic v2 y guardarlo como Metacritic durante un rollback.
- Los datos generales siguen Steam → Wikidata. El proveedor HTML público acotado
  de Deku devuelve crítica aunque no haya estimaciones HLTB. Verifica título/URL
  canónica/atribución, sin cookies, redirecciones ni claves; limita 1MB/10s, respeta
  pausas 403/429 y no contacta directamente con HLTB.
  La búsqueda envía explícitamente la preferencia pública plural `platforms`:
  `switch_2,switch,ps5,ps4,xbox_series,xbox_one,steam`. Omitirla limita la búsqueda
  anónima a Nintendo; `platform=all` en singular no la amplía. La revisión de
  caché propia del ámbito no reutiliza búsquedas vacías ni páginas del filtro
  anterior, no cambia `reviewVersion` y no elude la pausa compartida. Completista
  ausente no se rellena con Historia + Extras.
- Se omite automatizar SteamDB por su [FAQ](https://steamdb.info/faq/), sin evasión
  ni porcentajes calculados de usuarios presentados como crítica consultada. Las
  nuevas altas Steam y sincronizaciones con crítica vacía solo conservan Metacritic
  real y válido de Steam. No se reescriben notas existentes o históricas.
- El lote persistente de abajo fija hasta 1.000 destinos propios y usa estas mismas
  consultas/aplicaciones firmadas en secuencia. Una plaza activa única por cuenta
  impide lotes concurrentes; las lecturas respetan pausas de los proveedores. Un
  dato opcional ausente o una nota idéntica no se trata como identidad ambigua.
- La opción tras importar usa recibos nuevos/vinculados confirmados después de
  guardar sus ajustes. Fallar/reintentar afecta solo a información, no repite la
  importación. Progreso, notas, nota personal, fechas y vínculo Steam quedan fuera
  del plan. Refrescar imágenes verifica un solo rol, sin vaciar y sincronizar al jugador.

## Trabajos persistentes solicitados expresamente

### Imágenes fuera de Steam y errores de proveedor

Los roles logo/hero/cover ausentes usan SteamGridDB opcional y después páginas
públicas de LaunchBox sin clave, recursos oficiales de Nintendo y recursos
Wikidata/Wikimedia o capturas oficiales enlazadas por Deku. Las fuentes de imágenes
no aportan crítica ni datos de catálogo. `STEAMGRIDDB_API_KEY` /
`ArtworkSources:SteamGridDbApiKey` es un secreto opcional del backend: solo se envía
a la API documentada de SteamGridDB, nunca al CDN o al navegador. Sin él siguen
disponibles las demás fuentes.

Las consultas están acotadas y usan caché, HTTPS y una lista de hosts/rutas; no
envían cookies ni siguen redirecciones automáticas y respetan Retry-After/403/429.
La imagen debe decodificarse entera dentro de 8MB/20MP y tener dimensiones adecuadas
para su rol. El HTML público de LaunchBox se conserva seis horas y su verificación
de imagen, 24 horas. Reemplazar refresca esas cachés sin saltarse las pausas. Las
búsquedas vacías de Deku caducan en cinco minutos; su página aporta fecha e imágenes
aunque crítica/duraciones no necesiten cambios.

`chooseArtworkIdentity` pide elegir plataforma en LaunchBox; `editionMismatch`
avisa de ediciones diferentes sin mezclarlas. Los fallos devuelven
`wikidataUnavailable`, `catalogSourceUnavailable` o `artworkSourceUnavailable` y
conservan Retry-After; no se presentan como una búsqueda vacía confirmada.

```http
POST /api/metadata-autofill/jobs
Content-Type: application/json

{
  "clientRequestId": "a49f71b2-6db0-4b9d-8a91-79956e92cbcd",
  "gameIds": [101, 102],
  "overwrite": true,
  "acceptListedPlatformCritic": true
}
```

Devuelve 202 con el trabajo propio. Los IDs deben ser positivos, distintos y todos
de la cuenta; un ámbito inválido/mixto se rechaza entero. Misma cuenta/clave/datos
devuelve el mismo lote; cambiar esos datos o iniciar otro ámbito activo devuelve
409. Se admiten hasta 16 trabajos activos globales. La decisión de plataforma es
explícita: Reemplazar la explica y confirma una vez; vacíos empieza en false.
Acepta la nota atribuida de una edición con coincidencia fiable, no demuestra su
plataforma Steam. Metacritic Steam conserva prioridad y se guarda el proveedor real.

| Ruta | Función |
| --- | --- |
| `GET /api/metadata-autofill/jobs/current?includeItems=false` | Resumen activo/último no oculto de la cuenta; JSON `null` si no existe |
| `GET /api/metadata-autofill/jobs?skip=0&take=10` | Resúmenes del historial propio, incluidos avisos ocultos |
| `GET /api/metadata-autofill/jobs/{id}` | Estados, campos y avisos conservados por elemento |
| `POST /api/metadata-autofill/jobs/{id}/cancel` | Detener lo pendiente sin deshacer cambios confirmados |
| `POST /api/metadata-autofill/jobs/{id}/retry` | Reintento explícito de metadatos fallidos/parciales/cancelados |
| `POST /api/metadata-autofill/jobs/{id}/dismiss` | Ocultar el aviso inactivo, sin borrar historial |
| `POST /api/metadata-autofill/jobs/{id}/accept-listed-critics` | Aceptar juntas las dudas solo de plataforma; no resuelve ambigüedades reales |
| `POST /api/metadata-autofill/jobs/{id}/items/{gameId}/review` | Aplicar un `{token,fields}` nuevo y explícito y resolver solo ese elemento de forma atómica |

Todas las rutas exigen cuenta autenticada y `no-store`; un ID de trabajo ajeno da
404. Los DTO llevan códigos seguros, coincidencias públicas y nombres de campos
realmente guardados, nunca credenciales ni tokens de planes. Los contadores separan
procesados, actualizados, sin cambios, elecciones, parciales, fallidos y cancelados.
`completed` no significa que todas las fuentes opcionales aportasen datos. Puede
haber una elección pendiente junto a campos que sí se guardaron.

El worker no inicia escaneos por sí solo: consume trabajo explícito persistido,
reclama un lease de dos minutos y confirma cada elemento. Mutación del juego,
historial y recibo del lote comparten una transacción SQLite. Ante un resultado de
commit incierto decide el registro persistido, no un guardado repetido a ciegas.
Cerrar web/sesión detiene solo la observación; tras reiniciar la API se retoma lo
pendiente al caducar el lease anterior. El servidor debe estar encendido.

Las pausas Retry-After/cooldown se guardan por lote. Los avisos transitorios tienen
hasta tres intentos de lectura por elemento; los campos ya confirmados/coincidentes
no se vuelven a aplicar al reintentar. Una edición concurrente requiere revisión
nueva. Revisar un elemento conserva los demás y el lease de otra consulta activa.
Iniciar otro lote no borra el historial anterior. La copia SQLite incluye este
estado operativo; las transferencias CSV/ZIP no lo reanudan en otro destino.

Conserva las claves DataProtection desplegadas en privado. Prefiere volver a un
binario compatible manteniendo datos aditivos; puede hacer falta consultar otra vez.
Estos contratos no autorizan migrar, desplegar o restaurar sobre escrituras nuevas.
