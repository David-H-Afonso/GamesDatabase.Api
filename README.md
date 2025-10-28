# Games Database - API

A RESTful API built with ASP.NET Core for managing personal game collections. This backend provides comprehensive endpoints for game cataloging, user management, filtering, custom views, and data export capabilities.

## Features

- **RESTful API**: Full CRUD operations for games, platforms, statuses, and more
- **Multi-User Support**: JWT-based authentication with user-specific data isolation
- **Advanced Filtering**: Search with accent-insensitive matching, multi-criteria filtering, and sorting
- **Custom Views**: Create and save personalized filter configurations
- **Data Export**: Export game data in JSON or CSV formats
- **SQLite Database**: Lightweight, file-based database with Entity Framework Core
- **Referential Integrity**: Prevents deletion of catalog items in use
- **Automatic Scoring**: Calculated game scores based on configurable formulas

## Tech Stack

- **Framework**: ASP.NET Core 9.0
- **Database**: SQLite with Entity Framework Core
- **Authentication**: JWT (JSON Web Tokens)
- **API Documentation**: Swagger/OpenAPI
- **Language**: C# 12

## Prerequisites

- .NET 9.0 SDK or higher
- SQLite (included with EF Core)

## Local Installation

### Setup

1. **Clone the repository**

   ```bash
   git clone https://github.com/David-H-Afonso/GamesDatabase.Api
   cd GamesDatabase.Api
   ```

2. **Restore dependencies**

   ```bash
   dotnet restore
   ```

3. **Configure database**

   The application uses SQLite by default. The database will be created automatically on first run at `gamesdatabase.db`.

4. **Apply migrations**

   ```bash
   dotnet ef database update
   ```

5. **Configure settings** (optional)

   Edit `appsettings.json` to customize:

   - JWT settings (secret, issuer, audience, expiration)
   - CORS settings
   - Database path
   - Export file locations

## Development

### Run Development Server

```bash
dotnet run
```

The API will be available at:

- HTTP: `http://localhost:8080`
- Swagger UI: `http://localhost:8080/swagger`

### Available Commands

```bash
# Build the project
dotnet build

# Run tests
dotnet test

# Create a new migration
dotnet ef migrations add MigrationName

# Update database
dotnet ef database update
```

## Production Deployment

### Build for Production

```bash
dotnet publish -c Release -o ./publish
```

### Docker Support

A Dockerfile is provided for containerized deployments:

```bash
docker build -t gamesdatabase-api .
docker run -p 8080:8080 gamesdatabase-api
```

## API Endpoints

### Authentication

- `POST /api/users/register` - Register new user
- `POST /api/users/login` - User login
- `GET /api/users/recent` - Get recent users

### Games

- `GET /api/games` - List games with filtering and pagination
- `GET /api/games/{id}` - Get game details
- `POST /api/games` - Create new game
- `PUT /api/games/{id}` - Update game
- `DELETE /api/games/{id}` - Delete game

### Catalogs

- `GET /api/gameplatforms` - List platforms
- `GET /api/gamestatus` - List statuses
- `GET /api/gameplaywith` - List play modes
- `GET /api/gameplayedstatus` - List played statuses

### Views

- `GET /api/gameviews` - List custom views
- `POST /api/gameviews` - Create custom view
- `PUT /api/gameviews/{id}` - Update view
- `DELETE /api/gameviews/{id}` - Delete view

### Data Export

- `GET /api/dataexport/json` - Export as JSON
- `GET /api/dataexport/csv` - Export as CSV

For complete API documentation, run the application and visit `/swagger`.

## Configuration

### JWT Settings

```json
{
  "JwtSettings": {
    "SecretKey": "your-secret-key-min-32-characters",
    "Issuer": "GamesDatabase",
    "Audience": "GamesDatabase",
    "ExpirationMinutes": 43200
  }
}
```

### CORS Settings

```json
{
  "CorsSettings": {
    "AllowedOrigins": ["http://localhost:5173"]
  }
}
```

### Database Settings

```json
{
  "DatabaseSettings": {
    "DatabasePath": "gamesdatabase.db"
  }
}
```

## Database Schema

The application uses the following main entities:

- **Users**: User accounts with authentication
- **Games**: Game entries with metadata and ratings
- **GameStatus**: Game states (Playing, Completed, Wishlist, etc.)
- **GamePlatform**: Gaming platforms (PC, PlayStation, Xbox, etc.)
- **GamePlayWith**: Play modes (Solo, Co-op, Multiplayer, etc.)
- **GamePlayedStatus**: Completion status tracking
- **GameView**: Saved filter configurations
- **GamePlayWithMapping**: Many-to-many relationship for play modes

## Features Detail

### Advanced Search

- Case-insensitive and accent-insensitive search
- Searches across game names and comments
- Supports multiple accent variants (e.g., "pokemon" finds "Pokémon")

### Filtering Options

- Filter by status, platform, play mode, played status
- Year-based filtering (released, started, finished)
- Grade range filtering
- Exclude specific statuses

### Custom Views

- Save complex filter combinations
- Quick access to frequently used filters
- User-specific view management

### Price Tracking

- Mark games as cheaper via key stores or official stores
- Store URLs for key providers
- Filter by price comparison status

## Project Structure

```
GamesDatabase.Api/
├── Configuration/      # Application settings classes
├── Controllers/        # API endpoints
├── Data/              # Database context
├── DOCS/              # Documentation
├── DTOs/              # Data transfer objects
├── Helpers/           # Utility classes
├── Middleware/        # Custom middleware
├── Migrations/        # Entity Framework migrations
├── Models/            # Entity models
└── Services/          # Business logic services
```

## Migrations

The project includes migrations for:

- Initial schema creation
- Multi-user support
- Price comparison fields

To create a new migration:

```bash
dotnet ef migrations add YourMigrationName
```

## Contributing

Contributions are welcome. Please follow these steps:

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/new-feature`)
3. Commit your changes (`git commit -m 'Add new feature'`)
4. Push to the branch (`git push origin feature/new-feature`)
5. Open a Pull Request

## License

This project is licensed under the GPL-3.0 License.

---

**Backend API for Games Database application**
