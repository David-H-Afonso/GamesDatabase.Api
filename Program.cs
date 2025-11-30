using Microsoft.EntityFrameworkCore;
using GamesDatabase.Api.Data;
using GamesDatabase.Api.Configuration;
using GamesDatabase.Api.Middleware;
using GamesDatabase.Api.Services;
using GamesDatabase.Api.Models;
using GamesDatabase.Api.Helpers;

var builder = WebApplication.CreateBuilder(args);

// Load environment variables from .env file if it exists (for local development)
var envFilePath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
if (File.Exists(envFilePath))
{
    foreach (var line in File.ReadAllLines(envFilePath))
    {
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
            continue;

        var parts = line.Split('=', 2);
        if (parts.Length == 2)
        {
            Environment.SetEnvironmentVariable(parts[0].Trim(), parts[1].Trim());
        }
    }
}

// Override configuration with environment variables for NetworkSync
builder.Configuration["NetworkSync:Enabled"] = Environment.GetEnvironmentVariable("NETWORK_SYNC_ENABLED")
    ?? builder.Configuration["NetworkSync:Enabled"];
builder.Configuration["NetworkSync:NetworkPath"] = Environment.GetEnvironmentVariable("NETWORK_SYNC_PATH")
    ?? builder.Configuration["NetworkSync:NetworkPath"];
builder.Configuration["NetworkSync:Username"] = Environment.GetEnvironmentVariable("NETWORK_SYNC_USERNAME")
    ?? builder.Configuration["NetworkSync:Username"];
builder.Configuration["NetworkSync:Password"] = Environment.GetEnvironmentVariable("NETWORK_SYNC_PASSWORD")
    ?? builder.Configuration["NetworkSync:Password"];

// Override JWT settings from environment variables
builder.Configuration["JwtSettings:SecretKey"] = Environment.GetEnvironmentVariable("JWT_SECRET_KEY")
    ?? builder.Configuration["JwtSettings:SecretKey"];
builder.Configuration["JwtSettings:Issuer"] = Environment.GetEnvironmentVariable("JWT_ISSUER")
    ?? builder.Configuration["JwtSettings:Issuer"];
builder.Configuration["JwtSettings:Audience"] = Environment.GetEnvironmentVariable("JWT_AUDIENCE")
    ?? builder.Configuration["JwtSettings:Audience"];
if (int.TryParse(Environment.GetEnvironmentVariable("JWT_EXPIRATION_MINUTES"), out var expMinutes))
{
    builder.Configuration["JwtSettings:ExpirationMinutes"] = expMinutes.ToString();
}

builder.Services.Configure<CorsSettings>(
    builder.Configuration.GetSection(CorsSettings.SectionName));
builder.Services.Configure<DatabaseSettings>(
    builder.Configuration.GetSection(DatabaseSettings.SectionName));
builder.Services.Configure<ExportSettings>(
    builder.Configuration.GetSection(ExportSettings.SectionName));
builder.Services.Configure<JwtSettings>(
    builder.Configuration.GetSection(JwtSettings.SectionName));
builder.Services.Configure<DataExportOptions>(
    builder.Configuration.GetSection(DataExportOptions.SectionName));
builder.Services.Configure<NetworkSyncOptions>(
    builder.Configuration.GetSection(NetworkSyncOptions.SectionName));

// Add services to the container.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    });

// Configure Entity Framework
var databaseSettings = builder.Configuration.GetSection(DatabaseSettings.SectionName).Get<DatabaseSettings>() ?? new DatabaseSettings();

var databasePath = databaseSettings.DatabasePath;
if (!Path.IsPathRooted(databasePath))
{
    databasePath = Path.GetFullPath(databasePath);
}

var connectionString = $"Data Source={databasePath}";

builder.Services.AddDbContext<GamesDbContext>(options =>
{
    options.UseSqlite(connectionString);
    if (databaseSettings.EnableSensitiveDataLogging)
    {
        options.EnableSensitiveDataLogging();
    }
});

builder.Services.AddScoped<IViewFilterService, ViewFilterService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddHttpClient();
builder.Services.AddScoped<IZipExportService, ZipExportService>();
builder.Services.AddScoped<INetworkSyncService, NetworkSyncService>();
builder.Services.AddHttpContextAccessor();

// Configure HttpClient to trust development certificates
builder.Services.AddHttpClient("TrustAllCerts")
    .ConfigurePrimaryHttpMessageHandler(() =>
    {
        var handler = new HttpClientHandler();
        if (builder.Environment.IsDevelopment())
        {
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }
        return handler;
    })
    .ConfigureHttpClient(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(60);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
        client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
        client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br");
        client.DefaultRequestHeaders.Add("DNT", "1");
        client.DefaultRequestHeaders.Add("Connection", "keep-alive");
        client.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");
        client.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "document");
        client.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "navigate");
        client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "none");
        client.DefaultRequestHeaders.Add("Sec-Fetch-User", "?1");
        client.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
        {
            NoCache = false,
            MaxAge = TimeSpan.FromDays(365)
        };
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

var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>() ?? new JwtSettings();
builder.Services.AddAuthentication(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                System.Text.Encoding.UTF8.GetBytes(jwtSettings.SecretKey))
        };

        options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                // Only log if there's no token or if in debug mode
                if (builder.Environment.IsDevelopment())
                {
                    var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                    var authHeader = context.Request.Headers["Authorization"].ToString();
                    if (!string.IsNullOrEmpty(authHeader))
                    {
                        logger.LogDebug($"Authorization Header received: {authHeader.Substring(0, Math.Min(30, authHeader.Length))}...");
                    }
                }
                return Task.CompletedTask;
            },
            OnAuthenticationFailed = context =>
            {
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();

                // Check if it's a token expiration (common and expected)
                if (context.Exception.Message.Contains("expired") || context.Exception.Message.Contains("IDX10223"))
                {
                    logger.LogWarning("Token has expired - client needs to re-authenticate");
                }
                else
                {
                    // Other authentication failures are more concerning
                    logger.LogError($"Authentication failed: {context.Exception.Message}");
                }
                return Task.CompletedTask;
            },
            OnTokenValidated = context =>
            {
                // Only log successful validations in debug mode
                if (builder.Environment.IsDevelopment())
                {
                    var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                    var userId = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                    logger.LogDebug($"Token validated successfully for UserId: {userId}");
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title = "Games Database API",
        Version = "v2.0",
        Description = "API para gestión de base de datos de videojuegos con soporte multi-usuario"
    });

    // Configurar autenticación JWT en Swagger
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Description = "JWT Authorization header usando el esquema Bearer. Ejemplo: \"Bearer {token}\"",
        Name = "Authorization",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });

    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
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

app.UseMiddleware<ErrorHandlingMiddleware>();

// Ensure database is created and seed default data
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
    try
    {
        context.Database.Migrate();
        await SeedDefaultDataAsync(context);
    }
    catch (Exception)
    {
        throw;
    }
}

static async Task SeedDefaultDataAsync(GamesDbContext context)
{
    if (!context.Users.Any())
    {
        var adminUser = new User
        {
            Username = "Admin",
            PasswordHash = null,
            Role = UserRole.Admin,
            IsDefault = true
        };
        context.Users.Add(adminUser);
        await context.SaveChangesAsync();

        var platforms = new[]
        {
            new GamePlatform { Name = "Steam", Color = "#2a475e", SortOrder = 1, UserId = adminUser.Id },
            new GamePlatform { Name = "Epic Games", Color = "#2F2D2E", SortOrder = 2, UserId = adminUser.Id },
            new GamePlatform { Name = "GOG", Color = "#c99aff", SortOrder = 3, UserId = adminUser.Id },
            new GamePlatform { Name = "Itch.io", Color = "#de4660", SortOrder = 4, UserId = adminUser.Id },
            new GamePlatform { Name = "EA", Color = "#EA2020", SortOrder = 5, UserId = adminUser.Id },
            new GamePlatform { Name = "Ubisoft", Color = "#1472F1", SortOrder = 6, UserId = adminUser.Id },
            new GamePlatform { Name = "Battle.net", Color = "#009AE4", SortOrder = 7, UserId = adminUser.Id },
            new GamePlatform { Name = "Emulator", Color = "#d12e2e", SortOrder = 8, UserId = adminUser.Id },
            new GamePlatform { Name = "Nintendo Switch", Color = "#fe0016", SortOrder = 9, UserId = adminUser.Id }
        };
        context.GamePlatforms.AddRange(platforms);

        var statuses = new[]
        {
            new GameStatus { Name = "Pending", Color = "#be9c23", SortOrder = 1, UserId = adminUser.Id },
            new GameStatus { Name = "Next up", Color = "#793e77", SortOrder = 2, UserId = adminUser.Id },
            new GameStatus { Name = "DEFAULT_PLAYING", Color = "#61afef", SortOrder = 3, IsDefault = true, StatusType = SpecialStatusType.Playing, UserId = adminUser.Id },
            new GameStatus { Name = "Done", Color = "#3fc20f", SortOrder = 4, UserId = adminUser.Id },
            new GameStatus { Name = "Abandoned", Color = "#b91d1d", SortOrder = 5, UserId = adminUser.Id },
            new GameStatus { Name = "DEFAULT_NOT_FULFILLED", Color = "#919191", SortOrder = 6, IsDefault = true, StatusType = SpecialStatusType.NotFulfilled, UserId = adminUser.Id }
        };
        context.GameStatuses.AddRange(statuses);

        var playWiths = new[]
        {
            new GamePlayWith { Name = "Solo", Color = "#24c2b7", SortOrder = 1, UserId = adminUser.Id },
            new GamePlayWith { Name = "Friends", Color = "#ab32ec", SortOrder = 2, UserId = adminUser.Id },
            new GamePlayWith { Name = "Family", Color = "#099012", SortOrder = 3, UserId = adminUser.Id }
        };
        context.GamePlayWiths.AddRange(playWiths);

        var playedStatuses = new[]
        {
            new GamePlayedStatus { Name = "None", Color = "#b5b5b5", SortOrder = 1, UserId = adminUser.Id },
            new GamePlayedStatus { Name = "Some", Color = "#873ed0", SortOrder = 2, UserId = adminUser.Id },
            new GamePlayedStatus { Name = "Almost", Color = "#cc1eb5", SortOrder = 3, UserId = adminUser.Id },
            new GamePlayedStatus { Name = "Completed", Color = "#2ed42b", SortOrder = 4, UserId = adminUser.Id },
            new GamePlayedStatus { Name = "Abandoned", Color = "#a60808", SortOrder = 5, UserId = adminUser.Id }
        };
        context.GamePlayedStatuses.AddRange(playedStatuses);

        await context.SaveChangesAsync();
    }
}

app.UseHttpsRedirection();

if (app.Environment.IsDevelopment())
{
    app.UseCors("AllowAll");
}
else
{
    app.UseCors("AllowSpecificOrigins");
}

app.UseAuthentication();
app.UseUserContext();
app.UseAuthorization();

// Health check endpoint
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    version = "0.9.0",
    timestamp = DateTime.UtcNow
})).AllowAnonymous();

app.MapControllers();

app.Run();
