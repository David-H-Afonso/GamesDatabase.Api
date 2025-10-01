using Microsoft.EntityFrameworkCore;
using GamesDatabase.Api.Data;
using GamesDatabase.Api.Configuration;
using GamesDatabase.Api.Middleware;
using GamesDatabase.Api.Services;
using GamesDatabase.Api.Models;

var builder = WebApplication.CreateBuilder(args);

// Add configuration sections
builder.Services.Configure<CorsSettings>(
    builder.Configuration.GetSection(CorsSettings.SectionName));
builder.Services.Configure<DatabaseSettings>(
    builder.Configuration.GetSection(DatabaseSettings.SectionName));
builder.Services.Configure<ExportSettings>(
    builder.Configuration.GetSection(ExportSettings.SectionName));

// Add services to the container.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    });

// Configure Entity Framework
var databaseSettings = builder.Configuration.GetSection(DatabaseSettings.SectionName).Get<DatabaseSettings>() ?? new DatabaseSettings();

// Build connection string from database path
var databasePath = databaseSettings.DatabasePath;
if (!Path.IsPathRooted(databasePath))
{
    databasePath = Path.GetFullPath(databasePath);
}

var connectionString = $"Data Source={databasePath}";
Console.WriteLine($"Database connected successfully at: {connectionString}");

builder.Services.AddDbContext<GamesDbContext>(options =>
{
    options.UseSqlite(connectionString);
    if (databaseSettings.EnableSensitiveDataLogging)
    {
        options.EnableSensitiveDataLogging();
    }
});

// Add custom services
builder.Services.AddScoped<IViewFilterService, ViewFilterService>();

// Configure CORS
var corsSettings = builder.Configuration.GetSection(CorsSettings.SectionName).Get<CorsSettings>() ?? new CorsSettings();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSpecificOrigins", policy =>
    {
        if (corsSettings.AllowedOrigins.Any())
        {
            policy.WithOrigins(corsSettings.AllowedOrigins.ToArray())
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        }
        else
        {
            // Configuración por defecto en caso de que no haya orígenes configurados
            policy.AllowAnyOrigin()
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        }
    });

    // Política adicional para desarrollo
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// Add Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title = "Games Database API",
        Version = "v1",
        Description = "API para gestión de base de datos de videojuegos"
    });

    // Incluir comentarios XML para documentación
    // var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    // var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    // if (File.Exists(xmlPath))
    // {
    //     c.IncludeXmlComments(xmlPath);
    // }
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Games Database API v1");
        c.RoutePrefix = string.Empty; // Swagger en la raíz
    });
}

// Usar el middleware de manejo de errores personalizado en todos los entornos
app.UseMiddleware<ErrorHandlingMiddleware>();

// Ensure database is created and seed default data
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
    try
    {
        // Aplicar migraciones pendientes
        context.Database.Migrate();
        Console.WriteLine($"Database connected successfully at: {connectionString}");

        // Insertar datos por defecto solo si las tablas están vacías
        await SeedDefaultDataAsync(context);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error connecting to database: {ex.Message}");
    }
}

static async Task SeedDefaultDataAsync(GamesDbContext context)
{
    // Seed Game Platforms si la tabla está vacía
    if (!context.GamePlatforms.Any())
    {
        var platforms = new[]
        {
            new GamePlatform { Name = "Battle.net", Color = "#009AE4", IsActive = true, SortOrder = 1 },
            new GamePlatform { Name = "EA", Color = "#EA2020", IsActive = true, SortOrder = 2 },
            new GamePlatform { Name = "Emulador", Color = "#d12e2e", IsActive = true, SortOrder = 3 },
            new GamePlatform { Name = "Epic Games", Color = "#2F2D2E", IsActive = true, SortOrder = 4 },
            new GamePlatform { Name = "GOG", Color = "#c99aff", IsActive = true, SortOrder = 5 },
            new GamePlatform { Name = "Itch.io", Color = "#de4660", IsActive = true, SortOrder = 6 },
            new GamePlatform { Name = "Steam", Color = "#2a475e", IsActive = true, SortOrder = 7 },
            new GamePlatform { Name = "Switch", Color = "#fe0016", IsActive = true, SortOrder = 8 },
            new GamePlatform { Name = "Ubisoft", Color = "#1472F1", IsActive = true, SortOrder = 9 }
        };
        context.GamePlatforms.AddRange(platforms);
        await context.SaveChangesAsync();
        Console.WriteLine("Default platforms seeded successfully.");
    }

    // Seed Game Status si la tabla está vacía
    if (!context.GameStatuses.Any())
    {
        var statuses = new[]
        {
            new GameStatus { Name = "None", Color = "#8ca397", IsActive = true, SortOrder = 1, IsDefault = false, StatusType = SpecialStatusType.None },
            new GameStatus { Name = "Some", Color = "#d19a66", IsActive = true, SortOrder = 2, IsDefault = false, StatusType = SpecialStatusType.None },
            new GameStatus { Name = "Almost", Color = "#e5c07b", IsActive = true, SortOrder = 3, IsDefault = false, StatusType = SpecialStatusType.None },
            new GameStatus { Name = "Completed", Color = "#98c379", IsActive = true, SortOrder = 4, IsDefault = false, StatusType = SpecialStatusType.None },
            new GameStatus { Name = "Abandoned", Color = "#e06c75", IsActive = true, SortOrder = 5, IsDefault = true, StatusType = SpecialStatusType.NotFulfilled },
            new GameStatus { Name = "Playing", Color = "#61afef", IsActive = true, SortOrder = 6, IsDefault = true, StatusType = SpecialStatusType.Playing }
        };
        context.GameStatuses.AddRange(statuses);
        await context.SaveChangesAsync();
        Console.WriteLine("Default game statuses seeded successfully.");
    }
}

app.UseHttpsRedirection();

// Configurar CORS según el entorno
if (app.Environment.IsDevelopment())
{
    app.UseCors("AllowAll");
}
else
{
    app.UseCors("AllowSpecificOrigins");
}

app.UseAuthorization();

app.MapControllers();

app.Run();
