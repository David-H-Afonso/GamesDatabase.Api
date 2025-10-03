# 🎮 Games Database API - Frontend Integration Guide

**Version**: 2.0.0 (Multi-User Support)  
**Date**: October 3, 2025  
**Base URL**: `https://localhost:7245` (Development)

---

## 📑 Table of Contents

1. [Overview](#overview)
2. [Breaking Changes](#breaking-changes)
3. [Authentication](#authentication)
4. [User Management](#user-management)
5. [API Endpoints Reference](#api-endpoints-reference)
6. [Migration Guide](#migration-guide)
7. [Code Examples](#code-examples)
8. [Error Handling](#error-handling)

---

## 🎯 Overview

The API now implements **multi-user support** with **JWT-based authentication**. Each user has their own isolated database of games, platforms, statuses, and catalogs.

### Key Changes:

- ✅ JWT authentication required (or X-User-Id header for backward compatibility)
- ✅ Per-user data isolation (all games, platforms, etc. are scoped by user)
- ✅ Role-based access control (Admin vs Standard users)
- ✅ Default Admin user created automatically
- ✅ User management endpoints for admins

---

## ⚠️ Breaking Changes

### 1. Authentication Required

**Before**: All endpoints were publicly accessible  
**After**: All endpoints require user authentication

```javascript
// ❌ OLD - No authentication
fetch("/api/games");

// ✅ NEW - JWT authentication (recommended)
fetch("/api/games", {
  headers: {
    Authorization: `Bearer ${token}`,
  },
});

// ✅ NEW - Header authentication (temporary fallback)
fetch("/api/games", {
  headers: {
    "X-User-Id": "1",
  },
});
```

### 2. Data Scoping

All data is now **per-user**. Users only see their own:

- Games
- Platforms
- Statuses
- PlayWith options
- PlayedStatus options
- Views

### 3. Unique Constraints Changed

Unique constraints are now scoped by user:

**Before**: Platform names were globally unique  
**After**: Platform names are unique _per user_ (User 1 and User 2 can both have a platform named "Steam")

---

## 🔐 Authentication

### Default Admin Credentials

The system creates a default Admin user on first startup:

- **Username**: `Admin`
- **Password**: `null` (no password - leave the password field empty or send `null`)

### Login Flow

```javascript
// 1. Login to get JWT token
const loginResponse = await fetch("/api/users/login", {
  method: "POST",
  headers: {
    "Content-Type": "application/json",
  },
  body: JSON.stringify({
    username: "Admin",
    password: null, // Admin has no password by default
  }),
});

const { userId, username, role, token } = await loginResponse.json();

// 2. Store token in localStorage or sessionStorage
localStorage.setItem("authToken", token);
localStorage.setItem("userId", userId);
localStorage.setItem("userRole", role);

// 3. Use token in subsequent requests
const gamesResponse = await fetch("/api/games", {
  headers: {
    Authorization: `Bearer ${token}`,
    "Content-Type": "application/json",
  },
});
```

### Testing with Swagger

The API includes Swagger UI with JWT authentication support:

1. **Navigate to**: `https://localhost:7245/swagger`

2. **First, login to get a token**:

   - Expand the **`POST /api/users/login`** endpoint (this is the ONLY endpoint that doesn't require authentication)
   - Click **"Try it out"**
   - Enter the request body:
     ```json
     {
       "username": "Admin",
       "password": null
     }
     ```
   - Click **"Execute"**
   - Copy the `token` from the response (it's a long string starting with `eyJ...`)

3. **Authorize Swagger**:

   - Click the **"Authorize"** button (🔓 lock icon) at the top-right of the page
   - In the dialog, paste the token in the "Value" field
   - **Important**: Just paste the token directly - Swagger will automatically add "Bearer " prefix
   - Click **"Authorize"**
   - Click **"Close"**

4. **Now you can test all endpoints**:
   - The lock icon should now be closed (🔒)
   - All subsequent requests will automatically include the JWT token
   - Try the **`GET /api/users`** endpoint to verify it works

**Troubleshooting**:

- If you get `401 Unauthorized`, your token may have expired (tokens last 7 days)
- If you get `403 Forbidden`, your user doesn't have permission (e.g., trying to access admin-only endpoints)
- The default Admin user has no password - always use `null` for the password field when logging in

**Note**: The `/api/users/login` endpoint is marked with `[AllowAnonymous]` so it doesn't require authentication. All other endpoints require a valid JWT token.

### Token Expiration

- **Default expiration**: 7 days (10080 minutes)
- Token includes claims: `UserId`, `Username`, `Role`
- Expired tokens will return `401 Unauthorized`

### Logout

```javascript
// Simply remove the token
localStorage.removeItem("authToken");
localStorage.removeItem("userId");
localStorage.removeItem("userRole");
```

---

## 👥 User Management

### User Roles

| Role         | Capabilities                                                                                                                |
| ------------ | --------------------------------------------------------------------------------------------------------------------------- |
| **Admin**    | • Create, edit, delete users<br>• Change any user's password<br>• Cannot demote the last admin<br>• Full access to own data |
| **Standard** | • Can only change own password<br>• Cannot manage other users<br>• Full access to own data                                  |

### Default User

The system creates a default **Admin** user on first run:

- **Username**: `Admin`
- **Password**: `null` (no password required)
- **Role**: `Admin`
- **IsDefault**: `true` (cannot be deleted)

---

## 📚 API Endpoints Reference

### 🔑 User Endpoints

#### POST `/api/users/login`

Authenticate a user and receive JWT token

**Request**:

```json
{
  "username": "Admin",
  "password": null
}
```

**Response** (200 OK):

```json
{
  "userId": 1,
  "username": "Admin",
  "role": "Admin",
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
}
```

**Response** (401 Unauthorized):

```json
{
  "message": "Invalid username or password"
}
```

---

#### GET `/api/users`

Get all users (Admin only)

**Headers**: `Authorization: Bearer {token}`

**Response** (200 OK):

```json
[
  {
    "id": 1,
    "username": "Admin",
    "role": "Admin",
    "isDefault": true,
    "createdAt": "2025-10-02T23:54:02.6726034Z",
    "updatedAt": "2025-10-02T23:54:02.6726371Z"
  },
  {
    "id": 2,
    "username": "JohnDoe",
    "role": "Standard",
    "isDefault": false,
    "createdAt": "2025-10-02T23:55:15.1234567Z",
    "updatedAt": "2025-10-02T23:55:15.1234567Z"
  }
]
```

---

#### GET `/api/users/{id}`

Get a specific user (Admin or the user themselves)

**Headers**: `Authorization: Bearer {token}`

**Response** (200 OK):

```json
{
  "id": 1,
  "username": "Admin",
  "role": "Admin",
  "isDefault": true,
  "createdAt": "2025-10-02T23:54:02.6726034Z",
  "updatedAt": "2025-10-02T23:54:02.6726371Z"
}
```

---

#### POST `/api/users`

Create a new user (Admin only)

**Headers**: `Authorization: Bearer {token}`

**Request**:

```json
{
  "username": "JohnDoe",
  "password": "securePassword123", // Optional
  "role": "Standard" // "Admin" or "Standard"
}
```

**Response** (201 Created):

```json
{
  "id": 2,
  "username": "JohnDoe",
  "role": "Standard",
  "isDefault": false,
  "createdAt": "2025-10-02T23:55:15.1234567Z",
  "updatedAt": "2025-10-02T23:55:15.1234567Z"
}
```

**Notes**:

- When a user is created, default catalog data (platforms, statuses, etc.) is automatically seeded for them
- Password is optional (can be `null` or omitted)

---

#### PUT `/api/users/{id}`

Update a user (Admin only)

**Headers**: `Authorization: Bearer {token}`

**Request**:

```json
{
  "username": "JohnDoeUpdated",
  "role": "Admin"
}
```

**Response** (204 No Content)

**Errors**:

- `400 Bad Request`: Attempting to demote the last admin
- `409 Conflict`: Username already exists

---

#### DELETE `/api/users/{id}`

Delete a user (Admin only)

**Headers**: `Authorization: Bearer {token}`

**Response** (204 No Content)

**Errors**:

- `400 Bad Request`: Attempting to delete the default admin
- `400 Bad Request`: Attempting to delete the last admin

**Notes**:

- Deleting a user cascades and **deletes all their data** (games, platforms, etc.)

---

#### POST `/api/users/{id}/password`

Change a user's password

**Headers**: `Authorization: Bearer {token}`

**Request**:

```json
{
  "newPassword": "newSecurePassword456"
}
```

**Response** (204 No Content)

**Permissions**:

- **Admin**: Can change any user's password
- **Standard**: Can only change their own password

---

### 🎮 Game Endpoints

All game endpoints now filter by the authenticated user automatically.

#### GET `/api/games`

Get all games for the current user

**Headers**: `Authorization: Bearer {token}`

**Query Parameters**:

- `page` (int): Page number (default: 1)
- `pageSize` (int): Items per page (default: 10)
- `search` (string): Search by name or comment
- `statusId` (int): Filter by status
- `platformId` (int): Filter by platform
- `playWithId` (int): Filter by play-with
- `playedStatusId` (int): Filter by played status
- `sortBy` (string): Sort field (name, grade, score, etc.)
- `sortDescending` (bool): Sort descending
- `viewId` (int): Apply a saved view
- `viewName` (string): Apply a saved view by name

**Response** (200 OK):

```json
{
  "data": [
    {
      "id": 1,
      "name": "The Legend of Zelda",
      "statusId": 1,
      "status": { "id": 1, "name": "Playing", "color": "#61afef" },
      "platformId": 9,
      "platform": { "id": 9, "name": "Nintendo Switch", "color": "#fe0016" },
      "grade": 10,
      "critic": 95,
      "score": 9.75,
      "playWithIds": [1],
      "playWiths": [{ "id": 1, "name": "Solo", "color": "#24c2b7" }],
      "playedStatusId": 4,
      "playedStatus": { "id": 4, "name": "Completed", "color": "#2ed42b" },
      "released": "2017-03-03",
      "started": "2017-03-05",
      "finished": "2017-04-15",
      "comment": "Amazing open-world experience",
      "createdAt": "2025-10-02T23:55:00Z",
      "updatedAt": "2025-10-02T23:55:00Z"
    }
  ],
  "totalCount": 50,
  "page": 1,
  "pageSize": 10
}
```

---

#### GET `/api/games/{id}`

Get a specific game (only if it belongs to the current user)

**Headers**: `Authorization: Bearer {token}`

**Response** (200 OK): Same structure as above  
**Response** (404 Not Found): Game doesn't exist or doesn't belong to user

---

#### POST `/api/games`

Create a new game for the current user

**Headers**: `Authorization: Bearer {token}`

**Request**:

```json
{
  "name": "The Legend of Zelda",
  "statusId": 1,
  "platformId": 9,
  "grade": 10,
  "critic": 95,
  "story": 40,
  "completion": 120,
  "playWithIds": [1],
  "playedStatusId": 4,
  "released": "2017-03-03",
  "started": "2017-03-05",
  "finished": "2017-04-15",
  "comment": "Amazing open-world experience",
  "logo": "https://example.com/logo.png",
  "cover": "https://example.com/cover.png"
}
```

**Response** (201 Created): Returns the created game

**Errors**:

- `400 Bad Request`: Invalid StatusId, PlatformId, PlayedStatusId, or PlayWithIds for current user

---

#### PUT `/api/games/{id}`

Update a game (only if it belongs to the current user)

**Headers**: `Authorization: Bearer {token}`

**Request**: Same structure as POST (all fields optional)

**Response** (204 No Content)  
**Response** (404 Not Found): Game doesn't exist or doesn't belong to user

---

#### DELETE `/api/games/{id}`

Delete a game (only if it belongs to the current user)

**Headers**: `Authorization: Bearer {token}`

**Response** (204 No Content)  
**Response** (404 Not Found): Game doesn't exist or doesn't belong to user

---

### 🎯 Catalog Endpoints

All catalog endpoints (Platforms, Statuses, PlayWith, PlayedStatus) follow the same pattern:

#### GET `/api/gameplatforms`

Get all platforms for the current user

**Headers**: `Authorization: Bearer {token}` or `X-User-Id: 1`

**Query Parameters**:

- `page`, `pageSize`, `search`, `isActive`, `sortBy`, `sortDescending`

**Response** (200 OK):

```json
{
  "data": [
    {
      "id": 1,
      "name": "Steam",
      "color": "#2a475e",
      "isActive": true,
      "sortOrder": 1
    }
  ],
  "totalCount": 9,
  "page": 1,
  "pageSize": 10
}
```

#### GET `/api/gameplatforms/active`

Get only active platforms for the current user

**Headers**: `Authorization: Bearer {token}` or `X-User-Id: 1`

**Response** (200 OK):

```json
[
  {
    "id": 1,
    "name": "Steam",
    "color": "#2a475e",
    "isActive": true,
    "sortOrder": 1
  },
  {
    "id": 2,
    "name": "Epic Games",
    "color": "#2F2D2E",
    "isActive": true,
    "sortOrder": 2
  }
]
```

#### POST `/api/gameplatforms`

Create a new platform for the current user

**Headers**: `Authorization: Bearer {token}` or `X-User-Id: 1`

**Request**:

```json
{
  "name": "Xbox Game Pass",
  "color": "#107C10",
  "isActive": true
}
```

**Response** (201 Created)

#### POST `/api/gameplatforms/reorder`

Reorder platforms for the current user

**Headers**: `Authorization: Bearer {token}` or `X-User-Id: 1`

**Request**:

```json
{
  "orderedIds": [3, 1, 2, 4, 5]
}
```

**Response** (204 No Content)

**Similar endpoints exist for**:

- `/api/gamestatus` - Game statuses
- `/api/gameplaywith` - Play-with options
- `/api/gameplayedstatus` - Played statuses

---

### 📊 Export/Import Endpoints

#### GET `/api/dataexport/full`

Export all data for the current user

**Headers**: `Authorization: Bearer {token}` or `X-User-Id: 1`

**Response**: CSV file download containing:

- All platforms
- All statuses
- All play-with options
- All played statuses
- All views
- All games

**CSV Format**:

```csv
Type,Name,Status,Platform,PlayWith,PlayedStatus,Released,Started,Finished,Score,Critic,Grade,Completion,Story,Comment,Logo,Cover,Color,IsActive,SortOrder,IsDefault,StatusType,Description,FiltersJson,SortingJson,IsPublic,CreatedBy
Platform,Steam,,,,,,,,,,,,,,#2a475e,true,1,,,,,,,,
Status,Playing,,,,,,,,,,,,,#61afef,true,3,true,Playing,,,,,,
Game,The Legend of Zelda,Playing,Nintendo Switch,Solo,Completed,2017-03-03,2017-03-05,2017-04-15,9.75,95,10,120,40,Amazing experience,https://...,https://...,,,,,,,,,,
```

#### POST `/api/dataexport/full`

Import data for the current user

**Headers**: `Authorization: Bearer {token}` or `X-User-Id: 1`

**Request**: Form data with CSV file

```javascript
const formData = new FormData();
formData.append("csvFile", file);

await fetch("/api/dataexport/full", {
  method: "POST",
  headers: {
    Authorization: `Bearer ${token}`,
  },
  body: formData,
});
```

**Response** (200 OK):

```json
{
  "message": "Importación completa finalizada (modo MERGE)",
  "catalogs": {
    "platforms": { "imported": 2, "updated": 7 },
    "statuses": { "imported": 0, "updated": 6 },
    "playWiths": { "imported": 1, "updated": 2 },
    "playedStatuses": { "imported": 0, "updated": 5 }
  },
  "views": { "imported": 3, "updated": 1 },
  "games": { "imported": 45, "updated": 12 },
  "errors": null
}
```

**Notes**:

- Import mode is **MERGE**: existing items are updated, new items are created
- All imported data is assigned to the current user
- Special statuses (Playing, Not Fulfilled) are matched by `StatusType` + `IsDefault`, not by name

#### Legacy Data Import

The API automatically handles **legacy CSV exports** (from single-user version):

**How it works**:

1. CSV files without `UserId` column are supported
2. All imported data is automatically assigned to the **authenticated user**
3. If no authentication is provided, data is assigned to the **default Admin user** (UserId = 1)

**Example**:

```csv
Type,Name,Status,Platform,...
Platform,Steam,,,...
Game,The Legend of Zelda,Playing,Steam,...
```

When this legacy CSV is imported:

- If authenticated with JWT: data assigned to that user
- If using `X-User-Id: 5` header: data assigned to user 5
- If no authentication: data assigned to Admin (UserId = 1)

**Migration Strategy**:
For users migrating from single-user version:

1. Start the new multi-user API
2. Export data from old API (CSV)
3. Login to new API as Admin
4. Import the CSV - all data will be assigned to Admin user
5. Optionally create new users and redistribute games

---

## 🔄 Migration Guide

### Step 1: Update API Base URL (if needed)

```javascript
// config.js
export const API_BASE_URL = "https://localhost:7245/api";
```

### Step 2: Create API Helper with Authentication

```javascript
// api.js
class ApiService {
  constructor() {
    this.baseUrl = API_BASE_URL;
  }

  getHeaders() {
    const token = localStorage.getItem("authToken");
    return {
      "Content-Type": "application/json",
      ...(token && { Authorization: `Bearer ${token}` }),
    };
  }

  async get(endpoint, params = {}) {
    const queryString = new URLSearchParams(params).toString();
    const url = `${this.baseUrl}/${endpoint}${
      queryString ? `?${queryString}` : ""
    }`;

    const response = await fetch(url, {
      headers: this.getHeaders(),
    });

    if (response.status === 401) {
      // Token expired or invalid - redirect to login
      window.location.href = "/login";
      return;
    }

    if (!response.ok) {
      const error = await response.json();
      throw new Error(error.message || "API request failed");
    }

    return response.json();
  }

  async post(endpoint, data) {
    const response = await fetch(`${this.baseUrl}/${endpoint}`, {
      method: "POST",
      headers: this.getHeaders(),
      body: JSON.stringify(data),
    });

    if (response.status === 401) {
      window.location.href = "/login";
      return;
    }

    if (!response.ok) {
      const error = await response.json();
      throw new Error(error.message || "API request failed");
    }

    return response.status === 204 ? null : response.json();
  }

  async put(endpoint, data) {
    const response = await fetch(`${this.baseUrl}/${endpoint}`, {
      method: "PUT",
      headers: this.getHeaders(),
      body: JSON.stringify(data),
    });

    if (response.status === 401) {
      window.location.href = "/login";
      return;
    }

    if (!response.ok) {
      const error = await response.json();
      throw new Error(error.message || "API request failed");
    }

    return response.status === 204 ? null : response.json();
  }

  async delete(endpoint) {
    const response = await fetch(`${this.baseUrl}/${endpoint}`, {
      method: "DELETE",
      headers: this.getHeaders(),
    });

    if (response.status === 401) {
      window.location.href = "/login";
      return;
    }

    if (!response.ok) {
      const error = await response.json();
      throw new Error(error.message || "API request failed");
    }

    return null;
  }
}

export const api = new ApiService();
```

### Step 3: Create Login Component

```javascript
// LoginComponent.vue / LoginComponent.tsx
async function handleLogin(username, password) {
  try {
    const response = await fetch(`${API_BASE_URL}/users/login`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
      },
      body: JSON.stringify({ username, password }),
    });

    if (!response.ok) {
      throw new Error("Invalid credentials");
    }

    const { userId, username: user, role, token } = await response.json();

    // Store authentication data
    localStorage.setItem("authToken", token);
    localStorage.setItem("userId", userId);
    localStorage.setItem("username", user);
    localStorage.setItem("userRole", role);

    // Redirect to main app
    window.location.href = "/";
  } catch (error) {
    console.error("Login failed:", error);
    alert("Login failed: " + error.message);
  }
}
```

### Step 4: Update Existing API Calls

```javascript
// Before (no authentication)
const games = await fetch("/api/games").then((r) => r.json());

// After (with authentication helper)
const games = await api.get("games");

// Before (direct POST)
await fetch("/api/games", {
  method: "POST",
  headers: { "Content-Type": "application/json" },
  body: JSON.stringify(gameData),
});

// After (with authentication helper)
await api.post("games", gameData);
```

### Step 5: Add Auth Guards (Vue/React/Angular)

```javascript
// Router guard example (Vue Router)
router.beforeEach((to, from, next) => {
  const token = localStorage.getItem("authToken");

  if (to.path !== "/login" && !token) {
    next("/login");
  } else if (to.path === "/login" && token) {
    next("/");
  } else {
    next();
  }
});
```

### Step 6: Add Admin-Only Features

```javascript
// Check if current user is admin
function isAdmin() {
  return localStorage.getItem("userRole") === "Admin";
}

// Show/hide admin features
if (isAdmin()) {
  // Show user management button
  document.getElementById("userManagement").style.display = "block";
}
```

---

## 💻 Code Examples

### Complete Login Example

```html
<!DOCTYPE html>
<html>
  <head>
    <title>Games Database - Login</title>
  </head>
  <body>
    <h1>Login</h1>
    <form id="loginForm">
      <input type="text" id="username" placeholder="Username" required />
      <input type="password" id="password" placeholder="Password (optional)" />
      <button type="submit">Login</button>
    </form>

    <script>
      document
        .getElementById("loginForm")
        .addEventListener("submit", async (e) => {
          e.preventDefault();

          const username = document.getElementById("username").value;
          const password = document.getElementById("password").value || null;

          try {
            const response = await fetch(
              "https://localhost:7245/api/users/login",
              {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ username, password }),
              }
            );

            if (!response.ok) {
              throw new Error("Invalid credentials");
            }

            const { userId, token, role } = await response.json();

            localStorage.setItem("authToken", token);
            localStorage.setItem("userId", userId);
            localStorage.setItem("userRole", role);

            window.location.href = "/dashboard.html";
          } catch (error) {
            alert("Login failed: " + error.message);
          }
        });
    </script>
  </body>
</html>
```

### Fetch Games with Authentication

```javascript
async function loadGames(page = 1, pageSize = 20) {
  const token = localStorage.getItem("authToken");

  const response = await fetch(
    `https://localhost:7245/api/games?page=${page}&pageSize=${pageSize}`,
    {
      headers: {
        Authorization: `Bearer ${token}`,
      },
    }
  );

  if (response.status === 401) {
    // Token expired or invalid
    localStorage.clear();
    window.location.href = "/login.html";
    return;
  }

  const { data, totalCount, page: currentPage } = await response.json();

  // Display games
  displayGames(data);
  displayPagination(totalCount, currentPage, pageSize);
}
```

### Create a Game

```javascript
async function createGame(gameData) {
  const token = localStorage.getItem("authToken");

  const response = await fetch("https://localhost:7245/api/games", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      Authorization: `Bearer ${token}`,
    },
    body: JSON.stringify(gameData),
  });

  if (!response.ok) {
    const error = await response.json();
    throw new Error(error.message || "Failed to create game");
  }

  return response.json();
}

// Usage
try {
  const newGame = await createGame({
    name: "Elden Ring",
    statusId: 1,
    platformId: 1,
    grade: 10,
    critic: 96,
    playWithIds: [1],
    released: "2022-02-25",
  });
  console.log("Game created:", newGame);
} catch (error) {
  console.error("Error:", error.message);
}
```

### User Management (Admin Only)

```javascript
// List all users
async function getAllUsers() {
  const token = localStorage.getItem("authToken");

  const response = await fetch("https://localhost:7245/api/users", {
    headers: { Authorization: `Bearer ${token}` },
  });

  return response.json();
}

// Create a new user
async function createUser(username, password, role) {
  const token = localStorage.getItem("authToken");

  const response = await fetch("https://localhost:7245/api/users", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      Authorization: `Bearer ${token}`,
    },
    body: JSON.stringify({ username, password, role }),
  });

  if (!response.ok) {
    const error = await response.json();
    throw new Error(error.message);
  }

  return response.json();
}

// Change password
async function changePassword(userId, newPassword) {
  const token = localStorage.getItem("authToken");

  const response = await fetch(
    `https://localhost:7245/api/users/${userId}/password`,
    {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        Authorization: `Bearer ${token}`,
      },
      body: JSON.stringify({ newPassword }),
    }
  );

  if (!response.ok) {
    const error = await response.json();
    throw new Error(error.message);
  }
}
```

---

## ⚠️ Error Handling

### Common HTTP Status Codes

| Code | Meaning               | Action                                             |
| ---- | --------------------- | -------------------------------------------------- |
| 200  | OK                    | Request successful                                 |
| 201  | Created               | Resource created successfully                      |
| 204  | No Content            | Request successful (no response body)              |
| 400  | Bad Request           | Invalid request data                               |
| 401  | Unauthorized          | Token expired or invalid - redirect to login       |
| 403  | Forbidden             | Insufficient permissions                           |
| 404  | Not Found             | Resource doesn't exist or doesn't belong to user   |
| 409  | Conflict              | Duplicate resource (e.g., username already exists) |
| 500  | Internal Server Error | Server error - contact support                     |

### Error Response Format

```json
{
  "message": "Error description",
  "details": "Additional error details (optional)"
}
```

### Recommended Error Handler

```javascript
async function handleApiRequest(requestFunction) {
  try {
    return await requestFunction();
  } catch (error) {
    if (error.status === 401) {
      // Unauthorized - redirect to login
      localStorage.clear();
      window.location.href = "/login";
      return;
    }

    if (error.status === 403) {
      // Forbidden - show permission error
      alert("You do not have permission to perform this action");
      return;
    }

    if (error.status === 404) {
      // Not found
      alert("Resource not found");
      return;
    }

    // Generic error
    alert("An error occurred: " + (error.message || "Unknown error"));
    console.error("API Error:", error);
  }
}
```

---

## 📝 Notes & Best Practices

### Security

1. **Never expose JWT tokens** in console logs or error messages
2. **Store tokens securely** in `localStorage` or `sessionStorage` (not in cookies for this API)
3. **Always use HTTPS** in production
4. **Implement token refresh** if needed (current expiration is 7 days)
5. **Change the JWT SecretKey** in production (`appsettings.Production.json`)

### Performance

1. **Cache catalog data** (platforms, statuses) - they rarely change
2. **Implement pagination** for game lists
3. **Use debouncing** for search inputs
4. **Consider lazy loading** for images (logos, covers)

### User Experience

1. **Show loading indicators** during API calls
2. **Handle offline scenarios** gracefully
3. **Provide clear error messages** to users
4. **Implement auto-save** for forms (with debouncing)
5. **Add confirmation dialogs** for destructive actions (delete user, delete game)

### Data Integrity

1. **Validate user IDs** from catalog items match current user (the API does this, but double-check in UI)
2. **Handle special statuses carefully** (Playing, Not Fulfilled) - they cannot be deleted
3. **Warn users before deleting users** - all their data will be permanently deleted
4. **Export data before major operations** as a backup

---

## 🚀 Quick Start Checklist

### Backend Testing (Swagger)

- [ ] Open Swagger UI at `https://localhost:7245/swagger`
- [ ] Test login with Admin user (username: "Admin", password: null)
- [ ] Authorize Swagger with the JWT token
- [ ] Test authenticated endpoints

### Frontend Development

- [ ] Update API base URL configuration
- [ ] Create API service helper with authentication
- [ ] Implement login page/component
- [ ] Add authentication guards to routes
- [ ] Update all existing API calls to use authentication
- [ ] Add logout functionality
- [ ] Implement user management UI (for admins)
- [ ] Add password change UI
- [ ] Test with multiple users
- [ ] Verify data isolation between users
- [ ] Handle 401 errors globally (redirect to login)
- [ ] Update error handling for new API responses
- [ ] Test import/export with new user-scoped data
- [ ] Test legacy CSV import (without UserId)
- [ ] Document new authentication flow for your team

---

## 🆘 Support

If you encounter issues:

1. **Test with Swagger first**: `https://localhost:7245/swagger`
   - Verify endpoints work correctly
   - Check request/response formats
   - Test authentication flow
2. Check browser console for errors
3. Verify JWT token is being sent correctly
4. Check network tab for API responses
5. Ensure database migrations have been applied
6. Verify the API is running on the correct port

**Common Issues**:

- **401 Unauthorized**: Token expired or missing - redirect to login
- **404 Not Found**: Resource doesn't belong to current user
- **409 Conflict**: Duplicate username or catalog item name
- **CORS errors**: Ensure your frontend origin is in `appsettings.json` CorsSettings
- **Swagger not loading**: Check that the API is running on `https://localhost:7245`
- **Can't authorize in Swagger**: Make sure to login first using `/api/users/login` endpoint to get a token

**Swagger Authentication Steps**:

1. Expand `POST /api/users/login` endpoint (this is the ONLY unauthenticated endpoint)
2. Click "Try it out"
3. Use credentials: `{ "username": "Admin", "password": null }`
4. Click "Execute"
5. Copy the `token` from the response
6. Click the "Authorize" button (🔓) at the top-right
7. Paste the token (Swagger adds "Bearer" automatically)
8. Click "Authorize" then "Close"
9. All subsequent requests will include the JWT token automatically

**Important**: All endpoints except `/api/users/login` require authentication. Without a valid JWT token, you'll receive `401 Unauthorized`.

---

## 📋 Summary

**Key Points**:

- 🔐 **Default credentials**: Username `Admin`, Password `null` (no password)
- 🌐 **Swagger UI**: Available at `https://localhost:7245/swagger` with JWT support
- 📂 **Legacy imports**: Old CSV files (without UserId) are automatically assigned to the authenticated user
- 🔄 **User isolation**: All data is completely isolated per user
- 👑 **Admin powers**: Create/edit/delete users, but cannot delete last admin or default admin
- 🎯 **Backward compatibility**: `X-User-Id` header supported as fallback authentication method

---

**Happy Coding! 🎮**
