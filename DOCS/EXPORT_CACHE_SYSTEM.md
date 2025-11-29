# Sistema de Caché de Exportación ZIP

## Descripción

Sistema inteligente de exportación que minimiza el tiempo de exportación y descarga de imágenes evitando reexportar juegos sin cambios.

## Funcionamiento

### 1. Base de Datos

#### Tabla `game_export_cache`

Trackea el estado de exportación de cada juego:

```sql
CREATE TABLE game_export_cache (
  id INTEGER PRIMARY KEY,
  game_id INTEGER NOT NULL,
  last_exported_at TEXT NOT NULL,
  logo_downloaded INTEGER DEFAULT 0,
  cover_downloaded INTEGER DEFAULT 0,
  logo_url TEXT NULL,
  cover_url TEXT NULL,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL
)
```

#### Columna `game.modified_since_export`

Booleano que indica si el juego ha sido modificado desde la última exportación.

- **Se marca como `true`** automáticamente cuando se edita un juego (via `SaveChanges` en `GamesDbContext`)
- **Se marca como `false`** cuando el juego se exporta exitosamente

### 2. Lógica de Exportación

#### Exportación Normal (`full=false`, por defecto)

El sistema verifica para cada juego:

1. **¿Necesita reexportarse?**
   - El juego es nuevo (no tiene caché)
   - `ModifiedSinceExport = true`
2. **¿Las imágenes fallaron anteriormente?**
   - Logo no se descargó (`LogoDownloaded = false`) y la URL sigue siendo la misma
   - Cover no se descargó (`CoverDownloaded = false`) y la URL sigue siendo la misma

**Acciones:**

- ✅ **Juegos sin cambios y con imágenes OK**: Se omiten completamente
- ✅ **Juegos modificados**: Se exportan con `info.json` + imágenes
- ✅ **Solo fallos de imágenes**: Solo se reintentan las descargas de imágenes fallidas

#### Exportación Completa (`full=true`)

Ignora toda la caché y reexporta **todos** los juegos, incluyendo:

- Todos los archivos `info.json`
- Todas las imágenes (logo y cover)

Útil para:

- Primera exportación
- Regenerar todo el ZIP desde cero
- Debugging

### 3. Endpoint

```
GET /api/Export/zip?full={true|false}
```

**Parámetros:**

- `full` (opcional, default: `false`): Si es `true`, ignora la caché y exporta todo

**Ejemplos:**

```bash
# Exportación incremental (solo cambios)
curl -H "Authorization: Bearer TOKEN" \
  https://localhost:7245/api/Export/zip

# Exportación completa (todo)
curl -H "Authorization: Bearer TOKEN" \
  https://localhost:7245/api/Export/zip?full=true
```

### 4. Logging

El servicio registra información útil:

```
[Information] Starting ZIP export (fullExport: False)
[Information] Processing 150 games (fullExport: False)
[Debug] Skipping 'Game Name' - no changes since last export
[Information] Retrying logo download for 'Another Game'
[Information] Games processing complete: 25 exported, 125 skipped, 3 images retried
```

## Flujo de Trabajo

### Escenario 1: Primera Exportación

```
Usuario → GET /api/Export/zip
↓
Sistema detecta: No hay caché
↓
Exporta TODOS los juegos (150)
↓
Crea cache para cada juego
↓
ModifiedSinceExport = false
```

**Resultado:** ZIP completo, todas las descargas de imágenes

### Escenario 2: Exportación Incremental (después de editar 5 juegos)

```
Usuario edita 5 juegos en el frontend
↓
Backend: ModifiedSinceExport = true (automático)
↓
Usuario → GET /api/Export/zip
↓
Sistema verifica 150 juegos
- 5 con ModifiedSinceExport = true → EXPORTAR
- 145 sin cambios → OMITIR
↓
Actualiza caché de los 5 exportados
```

**Resultado:** Solo 5 juegos en el ZIP, descarga rápida

### Escenario 3: Reintentar Imágenes Fallidas

```
Exportación anterior:
- Juego A: logo OK, cover FALLÓ
- Juego B: logo FALLÓ, cover OK
↓
Usuario → GET /api/Export/zip
↓
Sistema detecta:
- Juego A: LogoDownloaded=true, CoverDownloaded=false → Reintentar cover
- Juego B: LogoDownloaded=false, CoverDownloaded=true → Reintentar logo
↓
Solo descarga las 2 imágenes que fallaron
```

**Resultado:** Imágenes recuperadas sin reexportar todo

### Escenario 4: Forzar Exportación Completa

```
Usuario → GET /api/Export/zip?full=true
↓
Sistema ignora TODA la caché
↓
Exporta los 150 juegos
↓
Actualiza toda la caché
```

**Resultado:** ZIP completo regenerado desde cero

## Ventajas

### 🚀 Rendimiento

- **Primera exportación:** ~2-3 minutos (150 juegos, 300 imágenes)
- **Exportación incremental (5 cambios):** ~5-10 segundos
- **Solo reintentos de imágenes:** ~2-3 segundos

### 💾 Eficiencia

- Reduce tráfico de red (no re-descarga imágenes que ya funcionaron)
- Reduce carga del servidor (no reprocesa juegos sin cambios)
- Reduce tamaño del ZIP (solo lo modificado)

### 🔄 Recuperación Automática

- Si una URL de imagen falla temporalmente, se reintenta en la próxima exportación
- No bloquea la exportación de otros juegos
- Logging claro de qué falló

## Consideraciones Técnicas

### Tracking Automático de Cambios

El sistema `SaveChanges` de Entity Framework detecta automáticamente modificaciones:

```csharp
private void UpdateTimestamps()
{
    var entities = ChangeTracker.Entries().Where(e => e.Entity is Game);

    foreach (var entry in entities)
    {
        if (entry.State == EntityState.Modified)
        {
            if (entry.Entity is Game game)
            {
                game.UpdatedAt = DateTime.UtcNow;
                game.ModifiedSinceExport = true; // 👈 Marca automática
            }
        }
    }
}
```

### Gestión de Caché

La caché se actualiza al finalizar cada exportación:

```csharp
// Después de exportar exitosamente
cache.UpdatedAt = DateTime.UtcNow;
cache.LastExportedAt = DateTime.UtcNow;
cache.LogoDownloaded = logoSuccess;
cache.CoverDownloaded = coverSuccess;
cache.LogoUrl = game.Logo;
cache.CoverUrl = game.Cover;

await _context.SaveChangesAsync();
```

### Detección de Cambios en URLs

Si un juego cambia la URL del logo o cover, el sistema lo detecta:

```csharp
bool logoNeedsRetry = !string.IsNullOrWhiteSpace(game.Logo) &&
    (cache == null ||
     (!cache.LogoDownloaded && cache.LogoUrl == game.Logo)); // 👈 Misma URL
```

## Casos de Uso

### Para el Usuario Final

1. **Edición diaria de juegos**
   - Editas 2-3 juegos al día
   - Exportación tarda segundos en lugar de minutos
2. **Backup completo mensual**
   - `?full=true` una vez al mes para tener un backup completo
3. **Recuperación de imágenes**
   - Si un CDN estuvo caído, la próxima exportación reintenta las imágenes fallidas

### Para el Desarrollador

1. **Debugging**

   - `?full=true` para regenerar todo desde cero
   - Logs muestran exactamente qué se exporta y qué se omite

2. **Testing**
   - Verificar que los cambios se detectan correctamente
   - Confirmar que las imágenes se descargan solo cuando es necesario

## Mantenimiento

### Limpiar Caché Antigua

Si necesitas resetear la caché manualmente:

```sql
-- Marcar todos los juegos como modificados
UPDATE game SET modified_since_export = 1;

-- O borrar toda la caché
DELETE FROM game_export_cache;
```

### Verificar Estado de Caché

```sql
-- Juegos pendientes de exportar
SELECT COUNT(*) FROM game WHERE modified_since_export = 1;

-- Imágenes que fallaron
SELECT g.name, gec.logo_downloaded, gec.cover_downloaded
FROM game g
JOIN game_export_cache gec ON g.id = gec.game_id
WHERE gec.logo_downloaded = 0 OR gec.cover_downloaded = 0;
```

## Futuras Mejoras Posibles

1. **Exportación de solo imágenes**
   - Endpoint separado para regenerar solo las imágenes
2. **Caché para Settings**
   - También trackear cambios en plataformas, estados, etc.
3. **Compresión diferencial**
   - Generar ZIPs incrementales (solo deltas)
4. **Limpieza automática**
   - Borrar caché de juegos eliminados
5. **Dashboard de caché**
   - Endpoint para ver estadísticas de caché
