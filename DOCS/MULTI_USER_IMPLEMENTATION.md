# Multi-User Implementation - Complete Guide

## 🎯 Overview

El sistema ahora soporta múltiples usuarios con autenticación JWT y control de acceso basado en roles. Cada usuario tiene su propia base de datos aislada de juegos, plataformas, estados, etc.

## 📋 Cambios Realizados

### 1. **Nuevo Modelo: User**

- **Ubicación**: `Models/User.cs`
- **Roles**: `Admin` y `Standard`
- **Características**:
  - Contraseña opcional (hasheada con BCrypt workFactor=12)
  - Usuario Admin por defecto no puede ser eliminado
  - Siempre debe existir al menos un Admin

### 2. **Autenticación JWT**

- **Token expiration**: 7 días por defecto (configurable)
- **Claims incluidos**: `NameIdentifier` (UserId), `Name` (Username), `Role`
- **Configuración**: `appsettings.json` → `JwtSettings`

### 3. **Todas las Entidades Ahora Tienen UserId**

Las siguientes entidades fueron modificadas para incluir `UserId`:

- `Game`
- `GamePlatform`
- `GameStatus`
- `GamePlayWith`
- `GamePlayedStatus`
- `GameView`

**Cambios en la base de datos**:

- Agregada columna `user_id` (INT, NOT NULL) a todas las tablas
- FK constraint con `CASCADE DELETE` (si se elimina usuario, se eliminan sus datos)
- Índices únicos actualizados para incluir `UserId` (ej: `UserId + Name`)

### 4. **Middleware de Autenticación**

- **UserContextMiddleware**: Extrae el UserId del JWT token o del header `X-User-Id`
- **Orden de prioridad**:
  1. JWT token (si está autenticado)
  2. Header `X-User-Id` (para retrocompatibilidad temporal)

### 5. **Nuevo Controller: UsersController**

**Endpoints**:

#### POST `/api/users/login`

```json
{
  "username": "Admin",
  "password": null
}
```

**Response**:

```json
{
  "userId": 1,
  "username": "Admin",
  "role": "Admin",
  "token": "eyJhbGciOiJIUzI1..."
}
```

#### GET `/api/users`

Lista todos los usuarios (solo Admin)

#### GET `/api/users/{id}`

Obtiene un usuario específico (Admin o el propio usuario)

#### POST `/api/users`

Crea un nuevo usuario (solo Admin)

```json
{
  "username": "JohnDoe",
  "password": "optional",
  "role": "Standard"
}
```

**Nota**: Al crear un usuario, automáticamente se le asignan datos por defecto (plataformas, estados, etc.)

#### PUT `/api/users/{id}`

Actualiza un usuario (solo Admin)

```json
{
  "username": "NewUsername",
  "role": "Admin"
}
```

#### DELETE `/api/users/{id}`

Elimina un usuario (solo Admin, no puede eliminar default admin o el último admin)

#### POST `/api/users/{id}/password`

Cambia la contraseña

```json
{
  "newPassword": "newPass123"
}
```

**Permisos**:

- Admin puede cambiar cualquier contraseña
- Standard solo puede cambiar su propia contraseña

---

## 🔄 Cambios en Controllers Existentes

### **PATRÓN GENERAL PARA TODOS LOS CONTROLLERS**

Todos los controllers deben:

1. Heredar de `BaseApiController` en lugar de `ControllerBase`
2. Filtrar queries por `UserId`
3. Asignar `UserId` al crear nuevas entidades
4. Usar `GetCurrentUserIdOrDefault(1)` para retrocompatibilidad temporal

### **Ejemplo: GamesController**

#### GET `/api/games`

```csharp
[HttpGet]
public async Task<ActionResult<PagedResult<GameDto>>> GetGames([FromQuery] GameQueryParameters parameters)
{
    var userId = GetCurrentUserIdOrDefault(1); // Fallback a user 1

    var query = _context.Games
        .Where(g => g.UserId == userId) // ⚠️ FILTRAR POR USERID
        .Include(g => g.Status)
        .Include(g => g.Platform)
        // ... resto del query

    // ... resto del código
}
```

#### POST `/api/games`

```csharp
[HttpPost]
public async Task<ActionResult<GameDto>> CreateGame([FromBody] GameCreateDto gameDto)
{
    var userId = GetCurrentUserIdOrDefault(1);

    // Verificar que Status, Platform, etc. pertenezcan al usuario
    var status = await _context.GameStatuses
        .FirstOrDefaultAsync(s => s.Id == gameDto.StatusId && s.UserId == userId);

    if (status == null)
        return BadRequest(new { message = "Invalid StatusId for current user" });

    var game = new Game
    {
        UserId = userId, // ⚠️ ASIGNAR USERID
        Name = gameDto.Name,
        StatusId = gameDto.StatusId,
        // ... resto de propiedades
    };

    _context.Games.Add(game);
    await _context.SaveChangesAsync();

    // ... resto del código
}
```

#### PUT `/api/games/{id}`

```csharp
[HttpPut("{id}")]
public async Task<IActionResult> UpdateGame(int id, [FromBody] GameUpdateDto gameDto)
{
    var userId = GetCurrentUserIdOrDefault(1);

    // Verificar que el juego pertenece al usuario
    var game = await _context.Games
        .FirstOrDefaultAsync(g => g.Id == id && g.UserId == userId);

    if (game == null)
        return NotFound();

    // Verificar que los nuevos IDs pertenezcan al usuario
    if (gameDto.StatusId.HasValue)
    {
        var statusExists = await _context.GameStatuses
            .AnyAsync(s => s.Id == gameDto.StatusId.Value && s.UserId == userId);
        if (!statusExists)
            return BadRequest(new { message = "Invalid StatusId for current user" });
    }

    // ... resto del código
}
```

#### DELETE `/api/games/{id}`

```csharp
[HttpDelete("{id}")]
public async Task<IActionResult> DeleteGame(int id)
{
    var userId = GetCurrentUserIdOrDefault(1);

    var game = await _context.Games
        .FirstOrDefaultAsync(g => g.Id == id && g.UserId == userId);

    if (game == null)
        return NotFound();

    _context.Games.Remove(game);
    await _context.SaveChangesAsync();

    return NoContent();
}
```

---

## 📝 Cambios en DTOs

No es necesario agregar `UserId` a los DTOs de respuesta (GameDto, etc.) ya que el UserId se maneja internamente en el backend. Los DTOs permanecen igual.

---

## 🔧 Cambios en ViewFilterService

El servicio de filtros debe respetar el UserId:

```csharp
public async Task<IQueryable<Game>> ApplyFiltersAsync(
    IQueryable<Game> query,
    ViewFilterConfiguration configuration,
    int userId) // ⚠️ NUEVO PARÁMETRO
{
    // Asegurar que solo se filtren juegos del usuario
    query = query.Where(g => g.UserId == userId);

    // ... resto del código de filtros
}
```

**Todos los métodos del servicio deben recibir `userId` y filtrar por él.**

---

## 📤 Cambios en DataExportController

### Export

```csharp
[HttpGet("full")]
public async Task<IActionResult> ExportFullDatabase()
{
    var userId = GetCurrentUserIdOrDefault(1);

    // Exportar SOLO datos del usuario actual
    var platforms = await _context.GamePlatforms
        .Where(p => p.UserId == userId)
        .ToListAsync();

    var games = await _context.Games
        .Where(g => g.UserId == userId)
        .Include(g => g.Status)
        // ... etc

    // ... resto del código
}
```

### Import

```csharp
[HttpPost("full")]
public async Task<IActionResult> ImportFullDatabase(IFormFile csvFile)
{
    var userId = GetCurrentUserIdOrDefault(1);

    // Al crear/actualizar entities, siempre asignar UserId
    var newPlatform = new GamePlatform
    {
        Name = record.Name,
        UserId = userId, // ⚠️ IMPORTANTE
        // ... resto
    };

    // Al buscar entities existentes, filtrar por UserId
    var existing = await _context.GamePlatforms
        .FirstOrDefaultAsync(p => p.Name == record.Name && p.UserId == userId);

    // ... resto del código
}
```

---

## 🗑️ Limpieza de Código

### Remover:

1. **Comentarios innecesarios** como:

   ```csharp
   // Navigation property - ignorada en JSON
   // Audit fields
   // Configure relationships
   ```

2. **Console.WriteLine** de debug en producción
3. **Código comentado** que no se usa
4. **Métodos obsoletos**

### Simplificar:

```csharp
// ❌ ANTES
if (something)
{
    return BadRequest();
}
else
{
    return Ok();
}

// ✅ DESPUÉS
if (something)
    return BadRequest();

return Ok();
```

---

## 🌐 Integración con Frontend

### 1. **Login Flow**

```javascript
// 1. Login
const response = await fetch("/api/users/login", {
  method: "POST",
  headers: { "Content-Type": "application/json" },
  body: JSON.stringify({ username: "Admin", password: null }),
});

const { userId, username, role, token } = await response.json();

// 2. Guardar token
localStorage.setItem("authToken", token);
localStorage.setItem("userId", userId);
```

### 2. **API Calls**

**Opción A: Usar JWT Token (recomendado)**

```javascript
const response = await fetch("/api/games", {
  headers: {
    Authorization: `Bearer ${localStorage.getItem("authToken")}`,
    "Content-Type": "application/json",
  },
});
```

**Opción B: Usar Header X-User-Id (temporal)**

```javascript
const response = await fetch("/api/games", {
  headers: {
    "X-User-Id": localStorage.getItem("userId"),
    "Content-Type": "application/json",
  },
});
```

### 3. **Gestión de Usuarios (Solo Admin)**

```javascript
// Listar usuarios
const users = await fetch("/api/users", {
  headers: { Authorization: `Bearer ${token}` },
});

// Crear usuario
await fetch("/api/users", {
  method: "POST",
  headers: {
    Authorization: `Bearer ${token}`,
    "Content-Type": "application/json",
  },
  body: JSON.stringify({
    username: "NewUser",
    password: "optional123",
    role: "Standard",
  }),
});

// Cambiar contraseña
await fetch(`/api/users/${userId}/password`, {
  method: "POST",
  headers: {
    Authorization: `Bearer ${token}`,
    "Content-Type": "application/json",
  },
  body: JSON.stringify({ newPassword: "newpass123" }),
});
```

---

## 🔐 Configuración de Producción

### appsettings.Production.json

```json
{
  "JwtSettings": {
    "SecretKey": "GENERATE_A_SECURE_RANDOM_KEY_HERE_AT_LEAST_32_CHARACTERS",
    "Issuer": "GamesDatabase.Api",
    "Audience": "GamesDatabase.Client",
    "ExpirationMinutes": 10080
  }
}
```

**⚠️ IMPORTANTE**: Cambiar el `SecretKey` en producción por uno generado aleatoriamente.

---

## 📊 Base de Datos

### Migración

```bash
# Eliminar base de datos actual (⚠️ CUIDADO: PERDERÁS TODOS LOS DATOS)
Remove-Item gamesdatabase.db

# Aplicar migración
dotnet ef database update

# El seeding creará automáticamente:
# - Usuario "Admin" (sin contraseña)
# - Plataformas por defecto
# - Estados por defecto
# - PlayWith por defecto
# - PlayedStatuses por defecto
```

### Esquema Actualizado

```
user
  - id (PK)
  - username (UNIQUE)
  - password_hash (NULL)
  - role (0=Standard, 1=Admin)
  - is_default
  - created_at
  - updated_at

game
  - id (PK)
  - user_id (FK → user.id, CASCADE)
  - status_id (FK)
  - name
  - ... (resto de columnas)

game_platform
  - id (PK)
  - user_id (FK → user.id, CASCADE)
  - name
  - ... (resto de columnas)
  - UNIQUE(user_id, name)

... (mismo patrón para todas las tablas)
```

---

## ✅ Checklist de Implementación

### Backend:

- [x] Modelo User creado
- [x] JWT configurado
- [x] Middleware UserContext
- [x] UsersController implementado
- [x] Todas las entidades tienen UserId
- [x] DbContext actualizado
- [x] Migración creada
- [x] Program.cs configurado con seeding
- [ ] GamesController actualizado con filtros UserId
- [ ] GamePlatformsController actualizado
- [ ] GameStatusController actualizado
- [ ] GamePlayWithController actualizado
- [ ] GamePlayedStatusController actualizado
- [ ] GameViewsController actualizado
- [ ] DataExportController actualizado
- [ ] ViewFilterService actualizado
- [ ] Código limpiado (comentarios, logs, etc.)

### Frontend:

- [ ] Pantalla de login
- [ ] Almacenamiento de token/userId
- [ ] Agregar Authorization header a todas las llamadas API
- [ ] Pantalla de gestión de usuarios (Admin)
- [ ] Cambio de contraseña
- [ ] Logout (borrar token)
- [ ] Manejo de 401 Unauthorized (redirect a login)

---

## 🚀 Próximos Pasos Sugeridos

1. **Aplicar la migración y verificar que el usuario Admin se crea correctamente**
2. **Actualizar cada controller siguiendo el patrón mostrado en este documento**
3. **Actualizar el ViewFilterService para recibir userId**
4. **Actualizar DataExportController para filtrar por userId**
5. **Limpiar código (remover comentarios, console.writeline, etc.)**
6. **Testing exhaustivo de todos los endpoints**
7. **Implementar el frontend de login y gestión de usuarios**

---

## 📞 Notas Importantes

1. **Retrocompatibilidad**: El sistema usa `GetCurrentUserIdOrDefault(1)` que asigna por defecto el UserId=1 si no se proporciona. Esto es TEMPORAL para migración. Una vez el frontend esté listo, cambiar a `CurrentUserId` que retorna null y requiere autenticación.

2. **Seguridad**:

   - Las contraseñas se hashean con BCrypt (workFactor=12)
   - JWT tokens expiran en 7 días por defecto
   - Contraseñas opcionales (para usuarios internos sin necesidad de auth)

3. **Aislamiento de Datos**: Cada usuario tiene su propia base de datos completamente aislada. No puede ver ni modificar datos de otros usuarios.

4. **Admin Protegido**: El usuario Admin por defecto no puede ser eliminado y siempre debe existir al menos un Admin en el sistema.

5. **Seeding Automático**: Al crear un nuevo usuario, automáticamente se le asignan plataformas, estados y catálogos por defecto para que pueda empezar a usar el sistema inmediatamente.

---

## 🐛 Troubleshooting

### "User authentication required"

- Verifica que estés enviando el header `X-User-Id` o el token JWT
- Verifica que el token no haya expirado
- Verifica que el userId existe en la base de datos

### "Invalid StatusId for current user"

- El Status/Platform/etc que intentas asignar no pertenece al usuario actual
- Verifica que estés usando IDs del usuario correcto

### "Cannot delete last admin user"

- Necesitas al menos un Admin en el sistema
- Crea otro Admin antes de eliminar el actual

---

Este documento cubre todos los aspectos de la implementación multi-usuario. Sigue el patrón establecido para actualizar los controllers restantes y tendrás un sistema completo y funcional.
