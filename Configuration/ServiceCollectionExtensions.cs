using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using GamesDatabase.Api.Infrastructure.Persistence;
using GamesDatabase.Api.Domain.Entities;
using GamesDatabase.Api.Application.Services;
using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Application.Services.Steam;
using GamesDatabase.Api.Authentication;
using Microsoft.AspNetCore.Authentication;

namespace GamesDatabase.Api.Configuration;

public static class ServiceCollectionExtensions
{
    public static WebApplicationBuilder LoadEnvironmentFile(this WebApplicationBuilder builder)
    {
        if (builder.Environment.IsEnvironment("Testing") || (builder.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("LocalDevelopment:SkipEnvironmentFile")))
        {
            return builder;
        }

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

        OverrideFromEnv(builder, "DatabaseSettings:DatabasePath", "GAMESDATABASE_DB_PATH");

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
        OverrideFromEnv(builder, "ArtworkSources:SteamGridDbApiKey", "STEAMGRIDDB_API_KEY");

        // Registered Household client configuration. These values are backend-only,
        // but unlike the removed static service token they contain no credential.
        OverrideFromEnv(builder, "HouseholdIntegration:ClientId", "HOUSEHOLD_CLIENT_ID");
        OverrideFromEnv(builder, "HouseholdIntegration:RedirectUris", "HOUSEHOLD_REDIRECT_URIS");
        OverrideFromEnv(builder, "HouseholdIntegration:AccessTokenMinutes", "HOUSEHOLD_ACCESS_TOKEN_MINUTES");
        OverrideFromEnv(builder, "HouseholdIntegration:RefreshTokenDays", "HOUSEHOLD_REFRESH_TOKEN_DAYS");
        OverrideFromEnv(builder, "HouseholdIntegration:AuthorizationCodeMinutes", "HOUSEHOLD_AUTHORIZATION_CODE_MINUTES");

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
        services.Configure<GameArtworkSettings>(configuration.GetSection(GameArtworkSettings.SectionName));
        services.AddOptions<HouseholdIntegrationOptions>()
            .Bind(configuration.GetSection(HouseholdIntegrationOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.ClientId), "Household client ID is required.")
            .Validate(options => options.GetRedirectUris().Count > 0, "At least one exact Household redirect URI is required.")
            .Validate(options => options.AccessTokenMinutes is > 0 and <= 60, "Household access-token lifetime must be 1-60 minutes.")
            .Validate(options => options.RefreshTokenDays is > 0 and <= 90, "Household refresh-token lifetime must be 1-90 days.")
            .Validate(options => options.AuthorizationCodeMinutes is > 0 and <= 10, "Household authorization-code lifetime must be 1-10 minutes.")
            .ValidateOnStart();

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
            })
            .AddScheme<AuthenticationSchemeOptions, HouseholdAccessTokenHandler>(
                HouseholdAccessTokenDefaults.AuthenticationScheme,
                _ => { });

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
                Version = $"v{typeof(Program).Assembly.GetName().Version?.ToString(3)}",
                Description = "Owned game libraries, playthroughs, Steam integrations and reviewed data workflows"
            });

            // Configure JWT authentication in Swagger
            c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.OpenApiSecurityScheme
            {
                Description = "JWT Authorization header using the Bearer scheme. Example: \"Bearer {token}\"",
                Name = "Authorization",
                In = Microsoft.OpenApi.ParameterLocation.Header,
                Type = Microsoft.OpenApi.SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT"
            });

            c.AddSecurityRequirement(document => new Microsoft.OpenApi.OpenApiSecurityRequirement
            {
                {
                    new Microsoft.OpenApi.OpenApiSecuritySchemeReference("Bearer", document),
                    []
                }
            });
        });

        return services;
    }

    public static IServiceCollection AddGamesDatabaseServices(this IServiceCollection services, IWebHostEnvironment environment, IConfiguration? configuration = null)
    {
        services.AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
                options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
            });
        services.AddMemoryCache();
        services.AddSingleton(TimeProvider.System);

        services.AddScoped<IViewFilterService, ViewFilterService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IGameService, GameService>();
        services.AddScoped<ICatalogService, CatalogService>();
        services.AddScoped<IGameViewService, GameViewService>();
        services.AddScoped<IPlaylistService, PlaylistService>();
        services.AddScoped<ICollectionService, CollectionService>();
        services.AddScoped<IGameDiscoveryService, GameDiscoveryService>();
        services.AddScoped<IOnboardingService, OnboardingService>();
        services.AddScoped<IGameMetadataService, GameMetadataService>();
        services.AddSingleton<MetadataAutofillSignal>();
        services.AddScoped<IMetadataAutofillJobService, MetadataAutofillJobService>();
        if (!environment.IsEnvironment("Testing")) services.AddHostedService<MetadataAutofillWorker>();
        services.AddScoped<IPublicGameMetadataProvider, PublicGameMetadataProvider>();
        services.AddHttpClient("game-metadata", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("GamesDatabase/3.0 (+https://github.com/David-H-Afonso/GamesDatabase.Api)");
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = System.Net.DecompressionMethods.All });
        services.AddSingleton<IGameDurationProvider, DekuDealsDurationProvider>();
        services.AddScoped<IGameArtworkProvider, GameArtworkProvider>();
        services.AddHttpClient("game-artwork", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(6);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("GamesDatabase/3.0 (+https://github.com/David-H-Afonso/GamesDatabase.Api)");
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, AutomaticDecompression = System.Net.DecompressionMethods.All });
        services.AddHttpClient("game-durations", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(8);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("GamesDatabase/3.0 (+https://github.com/David-H-Afonso/GamesDatabase.Api)");
            client.DefaultRequestHeaders.Accept.ParseAdd("text/html");
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, AutomaticDecompression = System.Net.DecompressionMethods.All });
        services.AddScoped<IGameImportExportService, GameImportExportService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IHouseholdIntegrationService, HouseholdIntegrationService>();
        services.AddHttpClient();
        services.AddScoped<IZipExportService, ZipExportService>();
        services.AddExportMediaDownloads(configuration);
        services.AddScoped<INetworkSyncService, NetworkSyncService>();
        services.AddScoped<IGameHistoryService, GameHistoryService>();
        services.AddScoped<IGameReplayService, GameReplayService>();
        services.AddHttpContextAccessor();
        services.AddScoped<ISteamApiService, SteamApiService>();
        services.AddScoped<ISteamStoreService, SteamStoreService>();
        services.AddScoped<ISteamArtworkService, SteamArtworkService>();
        services.AddScoped<ISteamSyncService, SteamSyncService>();
        services.AddScoped<SteamAchievementSyncService>();
        services.AddSingleton<SteamSyncJobService>();
        services.AddSingleton<ISteamSyncJobService>(provider => provider.GetRequiredService<SteamSyncJobService>());
        services.AddHostedService(provider => provider.GetRequiredService<SteamSyncJobService>());
        services.AddScoped<ISteamProfileService, SteamProfileService>();
        services.AddSingleton<ISteamAuthService, SteamAuthService>();

        // Scheduled backup background service
        services.AddSingleton<BackupScheduleService>();
        if (!(environment.IsDevelopment() && configuration?.GetValue<bool>("LocalDevelopment:DisableScheduledBackups") == true))
        {
            services.AddHostedService(sp => sp.GetRequiredService<BackupScheduleService>());
        }

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

    public static IServiceCollection AddGamesDatabaseDataProtection(this IServiceCollection services, IConfiguration configuration, bool isDesktopMode, IWebHostEnvironment? environment = null)
    {
        if (environment?.IsEnvironment("Testing") == true)
        {
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            return services;
        }
        var dataProtectionKeysPath = configuration["DataProtection:KeysPath"];
        var protection = services.AddDataProtection().SetApplicationName("GamesDatabase");
        if (!string.IsNullOrWhiteSpace(dataProtectionKeysPath))
        {
            Directory.CreateDirectory(dataProtectionKeysPath);
            protection.PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));
        }

        return services;
    }

    /// <summary>
    /// Adds rate limiting policies for security-sensitive endpoints.
    /// "auth" policy: 10 requests per minute per IP (login, refresh, Steam exchange).
    /// </summary>
    public static IServiceCollection AddGamesDatabaseRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Use only the server's connection identity, never caller-supplied
            // X-Forwarded-For/Forwarded headers. Normalize mapped IPv4 addresses.
            options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress is { } address
                    ? (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString()
                    : "unknown-connection",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

            options.AddFixedWindowLimiter("integration", limiterOptions =>
            {
                limiterOptions.PermitLimit = 60;
                limiterOptions.Window = TimeSpan.FromMinutes(1);
                limiterOptions.QueueLimit = 0;
            });
            options.AddPolicy("metadata", context => RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 12, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });

        return services;
    }

    private static void OverrideFromEnv(WebApplicationBuilder builder, string configKey, string envVar)
    {
        var value = Environment.GetEnvironmentVariable(envVar);
        if (value is not null) builder.Configuration[configKey] = value;
    }
}
