# ZIP Export Feature

## Descripción

Sistema de exportación completo de la base de datos de juegos en formato ZIP. Este endpoint genera un archivo ZIP estructurado que contiene:

- **Backup CSV completo** de todos los datos
- **Archivos JSON** con configuraciones (Platforms, Status, PlayWith, PlayedStatus, Views)
- **Carpetas individuales por juego** con información JSON e imágenes descargadas (logo y cover)

## Endpoint

```
GET /api/Export/zip
```

**Autenticación:** Requiere JWT token válido (header `Authorization: Bearer <token>`)

**Respuesta:**

- Tipo: `application/zip`
- Nombre del archivo: `games_database_export_YYYY-MM-DD.zip`

## Estructura del ZIP generado

```
Games Database/
├── Backups/
│   └── database_full_export_YYYY-MM-DD.csv
├── Settings/
│   ├── Platforms.json
│   ├── Status.json
│   ├── PlayWith.json
│   ├── PlayedStatus.json
│   └── Views.json
└── Games/
    ├── <Nombre_Juego_1>/
    │   ├── info.json
    │   ├── cover.png
    │   └── logo.png
    ├── <Nombre_Juego_2>/
    │   └── info.json
    └── ...
```

## Detalles de implementación

### 1. Configuración

En `appsettings.json` se ha añadido:

```json
"DataExport": {
  "FullExportUrl": "http://localhost:8080/api/DataExport/full"
}
```

Esta URL apunta al endpoint existente de exportación CSV completo.

### 2. Componentes creados

#### Models

- **`ExportRecord.cs`**: Modelo para parsear las filas del CSV usando CsvHelper

#### Configuration

- **`DataExportOptions.cs`**: Opciones de configuración para la URL del CSV

#### Services

- **`IZipExportService.cs`**: Interfaz del servicio
- **`ZipExportService.cs`**: Implementación completa del servicio que:
  - Descarga el CSV completo
  - Lo parsea con CsvHelper
  - Construye el ZIP en memoria con `ZipArchive`
  - Descarga imágenes (logo/cover) de forma segura
  - Maneja errores de descarga sin detener el proceso

#### Controllers

- **`ExportController.cs`**: Controlador con el endpoint `/api/Export/zip`

### 3. Registro de servicios

En `Program.cs` se registraron:

```csharp
// Configuración
builder.Services.Configure<DataExportOptions>(
    builder.Configuration.GetSection(DataExportOptions.SectionName));

// HttpClient para descargas
builder.Services.AddHttpClient();

// Servicio de exportación ZIP
builder.Services.AddScoped<IZipExportService, ZipExportService>();
```

### 4. Paquete NuGet añadido

```bash
dotnet add package CsvHelper
```

## Características técnicas

### Normalización de nombres de carpetas

Los nombres de juegos se convierten en nombres seguros para el sistema de archivos:

- Se eliminan caracteres inválidos: `/`, `\`, `?`, `*`, `:`, etc.
- Los espacios se convierten en guiones bajos `_`
- Si el nombre queda vacío, se usa `"Unknown_Game"`

**Ejemplo:**

- `"The Witcher 3: Wild Hunt"` → `"The_Witcher_3_Wild_Hunt"`
- `"Half-Life 2"` → `"Half-Life_2"`

### Manejo de imágenes

- Las URLs de `Logo` y `Cover` se descargan con `HttpClient`
- La extensión se detecta desde la URL (`.png`, `.jpg`, etc.)
- Si falla la descarga, se omite la imagen sin detener el proceso
- Por defecto usa `.png` si no se puede determinar la extensión

### Parseo de valores

El servicio incluye helpers para convertir strings del CSV:

```csharp
ParseBool(string value)   // "true", "1", "yes" → true
ParseInt(string value)    // "123" → 123
```

### Formato JSON

Los archivos JSON se generan con:

- Indentación para legibilidad (`WriteIndented = true`)
- Nombres en camelCase (`PropertyNamingPolicy = JsonNamingPolicy.CamelCase`)

## Uso desde el frontend

### JavaScript/TypeScript

```typescript
const response = await fetch("http://localhost:8080/api/Export/zip", {
  method: "GET",
  headers: {
    Authorization: `Bearer ${token}`,
  },
});

if (response.ok) {
  const blob = await response.blob();
  const url = window.URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = `games_database_export_${
    new Date().toISOString().split("T")[0]
  }.zip`;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  window.URL.revokeObjectURL(url);
}
```

### cURL

```bash
curl -H "Authorization: Bearer YOUR_JWT_TOKEN" \
     -o games_export.zip \
     http://localhost:8080/api/Export/zip
```

## Logging

El servicio registra información útil:

```
[Information] Downloading full export CSV from http://localhost:8080/api/DataExport/full
[Information] Parsing CSV content
[Information] Building ZIP archive with 150 records
[Information] Added backup CSV: database_full_export_2025-11-29.csv
[Information] Added settings files
[Information] Processing 75 games
[Debug] Downloading image from https://example.com/logo.png
[Warning] Failed to download image from https://example.com/broken.png
[Information] Processed all games
[Information] ZIP export generated successfully: games_database_export_2025-11-29.zip (5242880 bytes)
```

## Manejo de errores

Si ocurre un error durante la generación del ZIP, el endpoint devuelve:

```json
{
  "message": "Failed to generate ZIP export",
  "error": "Descripción del error"
}
```

HTTP Status: `500 Internal Server Error`

## Consideraciones de rendimiento

- El ZIP se construye **completamente en memoria** (`MemoryStream`)
- Las imágenes se descargan de forma **síncrona** (una tras otra)
- Para bases de datos muy grandes con muchas imágenes, el proceso puede tardar varios segundos
- Se recomienda implementar un timeout apropiado en el cliente

## Futuras mejoras posibles

1. **Descargas paralelas de imágenes** usando `Task.WhenAll`
2. **Caché de imágenes** para no descargarlas cada vez
3. **Streaming del ZIP** en lugar de construirlo todo en memoria
4. **Progreso de la operación** mediante SignalR o Server-Sent Events
5. **Compresión selectiva** (solo settings, solo juegos, etc.) mediante parámetros query
6. **Límite de tamaño** para evitar ZIPs muy grandes

## Testing

Para probar el endpoint:

1. Asegúrate de que el backend esté corriendo
2. Obtén un JWT token válido (login)
3. Llama al endpoint con Swagger, Postman o cURL
4. Verifica que el ZIP descargado tiene la estructura correcta
5. Extrae el ZIP y revisa los archivos JSON y las imágenes

## Dependencias

- **CsvHelper** (33.1.0): Para parsear el CSV de exportación
- **System.IO.Compression**: Para crear el archivo ZIP (incluido en .NET)
- **Microsoft.Extensions.Http**: Para HttpClientFactory (ya estaba instalado)
