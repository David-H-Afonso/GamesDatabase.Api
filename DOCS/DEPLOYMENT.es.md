# Despliegue y recuperación de la API

[English](DEPLOYMENT.md) · [README API](../README.es.md)

Es una lista operativa, no un despliegue ejecutado. Adopta primero la API compatible
y después el Front. Electron y el hosting real se verifican por separado.

## Publicación

El workflow de `main` valida versiones, restaura dependencias auditadas, compila/prueba
y publica imagen amd64/arm64. Calidad e imagen condicionan la release del HEAD exacto;
las notas `DOCS/releases/<versión>.md` forman su descripción. Tags/releases/artefactos
publicados son inmutables: no se mueven ni reemplazan con el mismo número.

Un push puede publicar versión y `latest`. No autoriza arrancar producción, migrar o
restaurar. Revisa antes cualquier sistema de actualización automática externo.

## Condiciones previas

1. Registra SHAs/versiones/digests nuevos y anteriores, arquitectura, montajes, rutas
   reales y migraciones pendientes. Verifica decodificación Skia/WebP nativa en la
   imagen Linux final; los tests Windows no son esa prueba.
2. Acuerda backup/rollback. Pausa escrituras y usa la API de copia SQLite o un
   procedimiento con servicio parado y tratamiento correcto de WAL. No copies
   solo el archivo principal de una base viva ignorando WAL/SHM.
3. Calcula hash, comprueba integridad/FKs y prueba restauración aislada. Revisa juegos,
   catálogos, listas/carpetas/capacidad/orden, vistas, Replays/historial, preferencias
   y login sin cambiar contraseñas. Conserva imagen/configuración anterior por digest.
4. CSV/ZIP es complementario, no una copia completa de auth/historial/caché. Bases,
   exports, claves y sesiones quedan fuera de repositorios y bundles web.

## Compatibilidad y configuración

Runtime .NET 10 LTS, Microsoft/EF 10, Swashbuckle 10 y SkiaSharp con assets nativos Linux.
Colección añade desarrollador nullable y preferencias propias Inicio/GOTY con revisión.
La guía añade estado propio, flag desactivado para cuentas existentes y procedencia de
ejemplos. Cambiar runtime o leer candidatos de imágenes no añade otra migración.

Las migraciones de revisión/recibos añaden uso UTC de vistas, modo aditivo, versión
de caché e IDs de alta reintentable. La migración posterior a 3.0.0 `AddPinnedQuickViews`
añade plazas nullable e índice único cuenta/plaza, dejando las vistas existentes sin
fijar y sin otros cambios. No forma parte de la imagen 3.0.0 original. Duraciones y
revisión de fechas no necesitan migración. Revisa también lo anterior pendiente.

El arranque aplica migraciones EF pendientes. Solo una base realmente vacía inicializa
el modelo actual y registra el baseline. Los valores Docker por defecto (8080, `/app/data`,
`/app/exports`) no prueban las rutas de una instancia. Configura deliberadamente
`GAMESDATABASE_DB_PATH`, firma/emisor/audiencia JWT, Steam, exports, imágenes/caché y
montajes. Persiste claves DataProtection en privado; Testing usa protección efímera.
Ninguna credencial debe formar parte de una URL del frontend.

La crítica v2 y las reparaciones acotadas auth/media no añaden migración. La cola
persistente posterior **sí añade `AddMetadataAutofillJobs`**, tabla SQLite propia
con ámbito congelado, idempotencia, recibos y leases. Inclúyela en copias consistentes.
El nuevo proceso solo retoma trabajo pendiente pedido expresamente; un lease vivo
anterior puede tardar hasta dos minutos en caducar. El binario antiguo ignora/pausa
esa cola: conserva la tabla aditiva al volver atrás; Down borraría trabajo/historial.
CSV/ZIP no encola ni reanuda trabajos operativos en otro destino. v2 usa otro
propósito DataProtection para que una API antigua no aplique OpenCritic como
Metacritic al volver atrás. Revisa [el contrato](METADATA_REVIEW.es.md); una revisión
antigua/caducada puede requerir simplemente consultar de nuevo.

Configura deliberadamente `STEAM_FRONTEND_BASE_URL`, `STEAM_CALLBACK_BASE_URL` y
orígenes CORS exactos. El nonce vincula frontend y `return_to`; un destino inválido
o sin configurar falla cerrado, sin confiar en parámetros del callback. El límite
de autenticación usa IP de conexión, no cabeceras reenviadas arbitrarias. Proxy de
confianza, Steam real y hosting siguen necesitando comprobación operativa.

ZIP usa otro transporte sin credenciales con límites de DNS/dirección/redirección,
tiempo/bytes e imagen decodificada. `ExportMedia:AllowedPrivateBaseUrls` puede
permitir un subárbol privado administrativo exacto, nunca su raíz completa.
`ImageSettings:BaseUrl` y `NetworkSync:ImageBaseUrl` solo conservan `/game-images/`;
no autorizan loopback/link-local ni eludir TLS. Comprueba el NAS real antes de
desplegar; las URLs rechazadas permanecen en CSV/metadatos. Los logos admiten raster
verificado y SVG pasivo con respuesta aislada. Servir un valor antiguo inseguro
devuelve404, sin reescribirlo.

Revisa administrador inicial, TLS, CORS/orígenes, cabeceras, límites y exposición antes
de compartir acceso. Arranca API y comprueba migración/salud/versión/lecturas propias;
después Front, imágenes, Guardar/Cancelar, Focus/estanterías, conflictos y exports.
No alteres datos ni sincronices para igualar un recuento esperado.

Comprueba recibos exactos de fechas y fin estable tras otra sincronización. Los bloqueos
de lote devuelven `503 databaseBusy` con `Retry-After`; conserva el recibo de una
importación correcta y reintenta solo ajustes pendientes. Reiniciar pierde estado
temporal de jobs, no juegos guardados; iniciar otro sync siempre es explícito.

## Reversión

Prefiere una imagen anterior compatible manteniendo esquema/datos aditivos. Down de
colección elimina metadatos/preferencias. Los ensayos aislados Up/Down/Up de revisión,
recibos y chinchetas conservan filas/FKs/secuencias, no los valores nuevos que se borran
al quitar campos. No ejecutes Down automáticamente ni lo llames rollback sin pérdida.

Si restaurar es necesario, para escrituras y acuerda cómo conservar/reconciliar lo
posterior. Presérvalo, restaura solo la copia verificada, comprueba integridad/FKs y usa
imagen compatible. No sobrescribas ediciones nuevas ni fuerces/muevas tags para
arreglar una release: corrige hacia delante.
