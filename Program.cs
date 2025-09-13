using Microsoft.EntityFrameworkCore;
using GamesDatabase.Api.Data;
using GamesDatabase.Api.Configuration;
using GamesDatabase.Api.Middleware;

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

// Ensure database is created
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
    try
    {
        context.Database.EnsureCreated();
        Console.WriteLine($"Database connected successfully at: {connectionString}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error connecting to database: {ex.Message}");
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
