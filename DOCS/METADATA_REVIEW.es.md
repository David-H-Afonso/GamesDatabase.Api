# Revisión de metadatos v2 con procedencia

[English](METADATA_REVIEW.md) · [Contratos de la guía](GUIDED_LIBRARY.md)

Esta capacidad no añade una migración. Pertenece al código actual; el README lo
distingue de las versiones publicadas.

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

Las identidades opcionales son `steamAppId`, `wikidataId` y `dekuDealsSlug`.
Valídalas y conserva las elecciones deliberadas al seleccionar otra fuente/edición.
Esta ruta no cambia el App ID Steam guardado. `reviewVersion` vale 1 por defecto;
los grupos valen true. La consulta tiene un límite de 12/minuto por cuenta.

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
la caché analizada de búsqueda/página de Deku de 24 horas, nunca sus bloqueos o
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
- No hay endpoint masivo, cola de servidor ni programador de metadatos. El lote
  explícito en memoria del Front fija hasta 1.000 destinos y ejecuta estas mismas
  operaciones en secuencia. Puede aceptar propuestas fiables, no identidades
  ambiguas ni Metacritic de otra plataforma en Deku sin revisión manual.
- La opción tras importar usa recibos nuevos/vinculados confirmados después de
  guardar sus ajustes. Fallar/reintentar afecta solo a información, no repite la
  importación. Progreso, notas, nota personal, fechas y vínculo Steam quedan fuera
  del plan. Refrescar imágenes verifica un solo rol, sin vaciar y sincronizar al jugador.

Conserva las claves DataProtection desplegadas en privado. Prefiere volver a un
binario compatible manteniendo datos aditivos; puede hacer falta consultar otra vez.
Estos contratos no autorizan migrar, desplegar o restaurar sobre escrituras nuevas.
