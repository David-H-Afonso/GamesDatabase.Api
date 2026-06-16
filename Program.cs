using Microsoft.EntityFrameworkCore;
using GamesDatabase.Api.Configuration;
using GamesDatabase.Api.Infrastructure.Persistence;
using GamesDatabase.Api.Middleware;

var builder = WebApplication.CreateBuilder(args);
var isDesktopMode = string.Equals(Environment.GetEnvironmentVariable("GDB_DESKTOP"), "true", StringComparison.OrdinalIgnoreCase);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

builder.LoadEnvironmentFile();
builder.ApplyEnvironmentOverrides();

builder.Services.BindConfigurationSections(builder.Configuration);
builder.Services.AddGamesDatabaseServices(builder.Environment);
builder.Services.AddGamesDatabaseDataProtection(builder.Configuration, isDesktopMode);
builder.Services.AddGamesDatabasePersistence(builder.Configuration);
builder.Services.AddGamesDatabaseCors(builder.Configuration, builder.Environment);
builder.Services.AddGamesDatabaseAuth(builder.Configuration, builder.Environment);
builder.Services.AddGamesDatabaseSwagger();

var app = builder.Build();

// Only expose Swagger in development or desktop mode
// In production, Swagger reveals the full API surface to attackers.
if (app.Environment.IsDevelopment() || isDesktopMode)
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Games Database API v1");
        c.RoutePrefix = "swagger";
    });
}

app.UseMiddleware<ErrorHandlingMiddleware>();

// Ensure database is created and seed default data
var dbHelper = new DatabaseStartupHelper();
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
    var startupLogger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        context.Database.Migrate();
    }
    catch (Exception ex)
    {
        startupLogger.LogError(ex, "Migration failed. Attempting to continue — the column may already exist.");
    }

    try
    {
        dbHelper.EnsureCompatibilitySchema(context, startupLogger);
    }
    catch (Exception ex)
    {
        startupLogger.LogError(ex, "Schema repair failed - startup will continue, but some features may not work correctly.");
    }

    try
    {
        await dbHelper.SeedDefaultDataAsync(context);
        await dbHelper.SeedMissingReplayTypesAsync(context);
    }
    catch (Exception ex)
    {
        startupLogger.LogError(ex, "Seeding failed.");
        throw;
    }
}

// NOTE: UseHttpsRedirection is intentionally omitted.
// The container only listens on HTTP (ASPNETCORE_URLS=http://+:8080).
// Enabling it would cause the /health curl check to follow a redirect to an
// HTTPS port that doesn't exist, making the container permanently unhealthy.

// Game images are now served by ImageProxyController at /game-images.
// It resizes + converts to WebP on demand and disk-caches the result,
// so we no longer need the static-file middleware for that path.

if (app.Environment.IsDevelopment() || isDesktopMode)
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