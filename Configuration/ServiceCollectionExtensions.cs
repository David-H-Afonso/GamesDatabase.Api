using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using GamesDatabase.Api.Infrastructure.Persistence;
using GamesDatabase.Api.Domain.Entities;
using GamesDatabase.Api.Application.Services;
using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Application.Services.Steam;

namespace GamesDatabase.Api.Configuration;

public static class ServiceCollectionExtensions
{
    public static WebApplicationBuilder LoadEnvironmentFile(this WebApplicationBuilder builder)
    {
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

        return builder;
    }

    public static WebApplicationBuilder ApplyEnvironmentOverrides(this WebApplicationBuilder builder)
    {
        // Override configuration with environment variables for NetworkSync
        OverrideFromEnv(builder, "NetworkSync:Enabled", "NETWORK_SYNC_ENABLED");
        OverrideFromEnv(builder, "NetworkSync:NetworkPath", "NETWORK_SYNC_PATH");
        OverrideFromEnv(builder, "NetworkSync:Username", "NETWORK_SYNC_USERNAME");
        OverrideFromEnv(builder, "NetworkSync:Password", "NETWORK_SYNC_PASSWORD");

        // Override JWT settings from environment variables
        OverrideFromEnv(builder, "JwtSettings:SecretKey", "JWT_SECRET_KEY");
        OverrideFromEnv(builder, "JwtSettings:Issuer", "JWT_ISSUER");
        OverrideFromEnv(builder, "JwtSettings:Audience", "JWT_AUDIENCE");
        if (int.TryParse(Environment.GetEnvironmentVariable("JWT_EXPIRATION_MINUTES"), out var expMinutes))
        {
            builder.Configuration["JwtSettings:ExpirationMinutes"] = expMinutes.ToString();
        }

        if (int.TryParse(Environment.GetEnvironmentVariable("JWT_REFRESH_TOKEN_EXPIRATION_DAYS"), out var refreshDays))
        {
            builder.Configuration["JwtSettings:RefreshTokenExpirationDays"] = refreshDays.ToString();
        }

        // Override Steam settings from environment variables
        OverrideFromEnv(builder, "SteamSettings:ApiKey", "STEAM_API_KEY");
        OverrideFromEnv(builder, "SteamSettings:CallbackBaseUrl", "STEAM_CALLBACK_BASE_URL");
        OverrideFromEnv(builder, "SteamSettings:FrontendBaseUrl", "STEAM_FRONTEND_BASE_URL");

        return builder;
    }

    public static IServiceCollection BindConfigurationSections(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<CorsSettings>(configuration.GetSection(CorsSettings.SectionName));
        services.Configure<DatabaseSettings>(configuration.GetSection(DatabaseSettings.SectionName));
        services.Configure<ExportSettings>(configuration.GetSection(ExportSettings.SectionName));
        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));
        services.Configure<DataExportOptions>(configuration.GetSection(DataExportOptions.SectionName));
        services.Configure<NetworkSyncOptions>(configuration.GetSection(NetworkSyncOptions.SectionName));
        services.Configure<SteamSettings>(configuration.GetSection(SteamSettings.SectionName));

        return services;
    }

    public static IServiceCollection AddGamesDatabasePersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var databaseSettings = configuration.GetSection(DatabaseSettings.SectionName).Get<DatabaseSettings>() ?? new DatabaseSettings();

        var databasePath = databaseSettings.DatabasePath;
        if (!Path.IsPathRooted(databasePath))
        {
            databasePath = Path.GetFullPath(databasePath);
        }

        var connectionString = $"Data Source={databasePath}";

        services.AddDbContext<GamesDbContext>(options =>
        {
            options.UseSqlite(connectionString);
            if (databaseSettings.EnableSensitiveDataLogging)
            {
                // EnableSensitiveDataLogging and SQL console logging are only safe in development.
                // In production they can expose query parameters (including user data) in logs.
                options.EnableSensitiveDataLogging();
                options.LogTo(Console.WriteLine, Microsoft.Extensions.Logging.LogLevel.Information);
            }
        });

        return services;
    }

    public static IServiceCollection AddGamesDatabaseAuth(this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        var jwtSettings = configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>() ?? new JwtSettings();

        if (!environment.IsDevelopment())
        {
            var knownDefaults = new[]
            {
                "ThisIsAVerySecureSecretKeyThatShouldBeChangedInProduction123456789",
                ""
            };

            if (knownDefaults.Contains(jwtSettings.SecretKey, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    "JWT SecretKey must be set to a secure value in production. " +
                    "Set the JWT_SECRET_KEY environment variable or update JwtSettings:SecretKey in configuration.");
            }
        }

        services.AddAuthentication(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme)
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
                        if (environment.IsDevelopment())
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

                        if (context.Exception.Message.Contains("expired") || context.Exception.Message.Contains("IDX10223"))
                        {
                            logger.LogWarning("Token has expired - client needs to re-authenticate");
                        }
                        else
                        {
                            logger.LogError($"Authentication failed: {context.Exception.Message}");
                        }
                        return Task.CompletedTask;
                    },
                    OnTokenValidated = context =>
                    {
                        if (environment.IsDevelopment())
                        {
                            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                            var userId = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                            logger.LogDebug($"Token validated successfully for UserId: {userId}");
                        }
                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorization();

        return services;
    }

    public static IServiceCollection AddGamesDatabaseCors(this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        var corsSettings = configuration.GetSection(CorsSettings.SectionName).Get<CorsSettings>() ?? new CorsSettings();
        services.AddCors(options =>
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
                    // No origins configured: deny all cross-origin requests.
                    // AllowAnyOrigin with credentials is invalid per the CORS spec and
                    // would create an open CORS policy that accepts any domain.
                    // Log a warning — admins should set CORS_ALLOWED_ORIGINS.
                    policy.WithOrigins("http://localhost:5173") // safe local-only fallback
                          .AllowAnyHeader()
                          .AllowAnyMethod();
                }
            });

            // Additional policy for development
            options.AddPolicy("AllowAll", policy =>
            {
                policy.AllowAnyOrigin()
                      .AllowAnyHeader()
                      .AllowAnyMethod();
            });
        });

        return services;
    }

    public static IServiceCollection AddGamesDatabaseSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new()
            {
                Title = "Games Database API",
                Version = "v2.0",
                Description = "API for video game database management with multi-user support"
            });

            // Configure JWT authentication in Swagger
            c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Description = "JWT Authorization header using the Bearer scheme. Example: \"Bearer {token}\"",
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
        });

        return services;
    }

    public static IServiceCollection AddGamesDatabaseServices(this IServiceCollection services, IWebHostEnvironment environment)
    {
        services.AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
                options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
            });
        services.AddMemoryCache();

        services.AddScoped<IViewFilterService, ViewFilterService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IGameService, GameService>();
        services.AddScoped<ICatalogService, CatalogService>();
        services.AddScoped<IGameViewService, GameViewService>();
        services.AddScoped<IGameImportExportService, GameImportExportService>();
        services.AddScoped<IUserService, UserService>();
        services.AddHttpClient();
        services.AddScoped<IZipExportService, ZipExportService>();
        services.AddScoped<INetworkSyncService, NetworkSyncService>();
        services.AddScoped<IGameHistoryService, GameHistoryService>();
        services.AddScoped<IGameReplayService, GameReplayService>();
        services.AddHttpContextAccessor();
        services.AddScoped<ISteamApiService, SteamApiService>();
        services.AddScoped<ISteamStoreService, SteamStoreService>();
        services.AddScoped<ISteamSyncService, SteamSyncService>();
        services.AddScoped<ISteamProfileService, SteamProfileService>();
        services.AddSingleton<ISteamAuthService, SteamAuthService>();

        // Scheduled backup background service
        services.AddSingleton<BackupScheduleService>();
        services.AddHostedService(sp => sp.GetRequiredService<BackupScheduleService>());

        // Configure HttpClient for CSV exports (longer timeout)
        services.AddHttpClient("TrustAllCerts")
            .ConfigurePrimaryHttpMessageHandler(() =>
            {
                var handler = new HttpClientHandler();
                if (environment.IsDevelopment())
                {
                    handler.ServerCertificateCustomValidationCallback =
                        HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
                }
                return handler;
            })
            .ConfigureHttpClient(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(120);
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

        // Configure HttpClient for images (shorter timeout to avoid hanging)
        services.AddHttpClient("ImageDownloader")
            .ConfigurePrimaryHttpMessageHandler(() =>
            {
                var handler = new HttpClientHandler();
                if (environment.IsDevelopment())
                {
                    handler.ServerCertificateCustomValidationCallback =
                        HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
                }
                return handler;
            })
            .ConfigureHttpClient(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                client.DefaultRequestHeaders.Add("Accept", "image/avif,image/webp,image/apng,image/svg+xml,image/*,*/*;q=0.8");
                client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
                client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br");
                client.DefaultRequestHeaders.Add("DNT", "1");
                client.DefaultRequestHeaders.Add("Connection", "keep-alive");
                client.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "image");
                client.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "no-cors");
                client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "cross-site");
                client.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
                {
                    NoCache = false,
                    MaxAge = TimeSpan.FromHours(1)
                };
            });

        return services;
    }

    public static IServiceCollection AddGamesDatabaseDataProtection(this IServiceCollection services, IConfiguration configuration, bool isDesktopMode)
    {
        var dataProtectionKeysPath = configuration["DataProtection:KeysPath"];
        if (isDesktopMode && !string.IsNullOrWhiteSpace(dataProtectionKeysPath))
        {
            Directory.CreateDirectory(dataProtectionKeysPath);
            services.AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath))
                .SetApplicationName("GamesDatabase");
        }

        return services;
    }

    private static void OverrideFromEnv(WebApplicationBuilder builder, string configKey, string envVar)
    {
        builder.Configuration[configKey] = Environment.GetEnvironmentVariable(envVar)
            ?? builder.Configuration[configKey];
    }
}