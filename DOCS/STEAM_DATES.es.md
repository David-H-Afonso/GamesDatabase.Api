# Referencias de fechas Steam revisadas

[English](STEAM_DATES.md) · [README API](../README.es.md) · [Progreso Steam](STEAM_ACHIEVEMENT_SETS.md)

## Qué significan estas fechas

El tiempo Steam es acumulado, no un calendario de sesiones. El primer logro prueba que
ya se había jugado en ese momento, pero no necesariamente la primera partida. La última
partida y el último logro guardado **no** prueban cuándo terminó la historia.

La API ofrece **referencias para campos vacíos**. El usuario elige Inicio/Fin por separado.
Leer, abrir una ficha, recargar referencias o sincronizar Steam no rellena ni desplaza
automáticamente esas fechas personales. Una fecha existente, incluso aceptada con el
flujo Steam antiguo, se conserva. Para cambiarla deliberadamente se utiliza el editor
normal de datos personales.

Steam puede omitir `rtime_last_played` de otras cuentas aunque sus juegos o logros sean
visibles. No se piden claves ajenas ni se eluden restricciones de privacidad. La revisión
2 puede ofrecer el último logro válido de caché como referencia alternativa claramente
identificada; si tampoco existe, la fecha sigue siendo desconocida.

## Consultar referencias

Con autenticación y alcance de la cuenta:

```http
GET /api/steam/date-suggestions?gameId=17&includeStarted=true&reviewVersion=2&timeZone=Europe%2FMadrid
```

Sin `gameId` se consulta toda la cuenta. La web actual pagina la revisión en 40 juegos,
busca por nombre propio/Steam y selecciona solo campos visibles.

| Campo | Significado |
| --- | --- |
| `reviewVersion: 2` / `reviewTimeZone` | Capacidad del cliente y zona horaria indicada para la revisión |
| `gameId`, `gameName`, `steamAppId`, `steamName` | Juego propio e identidad Steam vinculada |
| `updatedAt` | Timestamp UTC del juego revisado |
| `currentStarted`, `currentFinished` | Fechas personales guardadas; consultar no las reemplaza |
| `proposedStarted`, `proposedFinished` | Referencias opcionales para campos vacíos |
| `startedSource`, `finishedSource` | `firstAchievement`, `lastPlayed`, `lastAchievement` o `none` |
| `observedLastPlayed`, `firstAchievementDate`, `lastAchievementDate` | Evidencia disponible de actividad, no declaraciones de completado |
| `notes` | Códigos informativos estables; el cliente traduce los conocidos, no muestra texto bruto del servidor |

Se rechazan Unix 0/1, timestamps fuera de rango/futuros y anteriores a Steam. Los valores
antiguos inválidos se filtran **antes** de elegir el primer/último logro; un timestamp malo
no puede descartar toda la biblioteca Steam. Tampoco se propone una referencia que
contradiga la otra fecha personal ya guardada.

El navegador envía su zona y la API convierte una vez los eventos UTC originales en
fechas de calendario. Una fecha aceptada no se reconvierte al volver a leerla. Esto importa
a medianoche, al cambiar de año y con el horario de verano. Las zonas o versiones de
revisión inválidas devuelven `400`. Los clientes antiguos usan revisión 1 y no reciben la
nueva alternativa de último logro que no saben explicar correctamente.

Un resultado vacío significa **sin nuevas referencias fiables**, no todas las fechas
completas ni ausencia de partidas. Sin cuenta Steam vinculada se devuelve `400`.
Consultar un juego ausente/ajeno no revela registros de otra cuenta.

## Guardar exactamente los campos revisados

```http
POST /api/steam/date-suggestions/apply
Content-Type: application/json

{
  "timeZone": "Europe/Madrid",
  "suggestions": [
    {
      "gameId": 17,
      "finished": "2026-06-02",
      "expectedUpdatedAt": "2026-11-26T17:00:00Z"
    }
  ]
}
```

Omitir `started` lo conserva. Se aceptan como máximo 1.000 juegos distintos por petición.
Los valores deben ser fechas ISO reales, no referencias futuras ni pares inicio/fin
invertidos. Propiedad y timestamp revisado se comprueban en una transacción corta. Los
clientes de revisión 2 siempre envían `expectedUpdatedAt`. Los campos ocupados se conservan;
no hay sobrescritura implícita, tampoco para el antiguo origen de fin `steam`.

La respuesta contiene `updated`, `errors`, recibos `applied` exactos por campo (`gameId`,
inicio/fin aceptados y nuevo `updatedAt` UTC) y motivos `skipped`: `changed`, `unavailable`,
`invalidDate`, `hasDate`. Un `200` con cero juegos actualizados no es un guardado correcto de
fechas. La web conserva la revisión y exige recargar ante resultados obsoletos/parciales/
inciertos; no repite escrituras automáticamente.

Los cambios reales generan historial de campo. Un fin revisado usa el marcador informativo
`steam-reviewed`; volver a jugar o sincronizar no puede moverlo a otro año. Notas,
estimaciones, tiempo, estado y demás campos personales no se recalculan como efecto
secundario. Este flujo no necesita migración de esquema.

Los checks quedan vinculados a su referencia y timestamp exactos. Una recarga ajena no
traslada silenciosamente la selección a una fecha nueva.

## Ignorar referencias

`POST /api/steam/date-suggestions/dismiss` recibe la misma zona y juegos/fechas/timestamps
seleccionados. Guarda rechazos propios, no fechas personales. La misma referencia rechazada
no se vuelve a ofrecer. Ante confirmación parcial/incierta se recarga; no se supone que
se haya guardado todo el lote.

## Por qué el recuento anual no coincide con toda la biblioteca

Estadísticas usa Inicio o Fin guardados de juegos y Replays. El criterio está visible en
Resumen, Mes/año y rankings del período, y se conserva al abrir un mes. Un juego sin la
fecha correspondiente no se asigna a un año adivinando desde el tiempo total. La actividad
de logros fechados tiene su propia vista.

Se pueden tener muchos juegos/logros fechados y ningún fin guardado. Revisa las referencias
o introduce las fechas personales conocidas; no se infla el contador para igualarlo al
tamaño de la colección.

## Ajustes de importación Steam y concurrencia

Importar Steam y su posterior `PATCH /api/games/bulk` son dos pasos confirmados por separado.
El lote reserva una transacción SQLite corta antes de leer: una escritura de caché
competidora termina antes de aplicar ajustes. Los catálogos deben ser de la misma cuenta.
Repetir ajustes iguales confirma la fila sin falsear su fecha de edición; modificar solo
relaciones sigue siendo una edición real.

Los bloqueos SQLite devuelven `503 databaseBusy` saneado con `Retry-After: 1`, no `400` de
datos inválidos. No se oculta el bloqueo repitiendo toda una importación correcta: se
conserva su recibo y solo se revisan/reintentan ajustes pendientes cuando proceda.

Fuentes: [Steam IPlayerService](https://partner.steamgames.com/doc/webapi/IPlayerService),
[Transacciones SQLite de Microsoft](https://learn.microsoft.com/dotnet/standard/data/sqlite/transactions).
