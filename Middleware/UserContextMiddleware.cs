using System.Security.Claims;

namespace GamesDatabase.Api.Middleware;

public class UserContextMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<UserContextMiddleware> _logger;

    public UserContextMiddleware(RequestDelegate next, ILogger<UserContextMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        int? userId = null;

        if (context.User.Identity?.IsAuthenticated == true)
        {
            var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim != null)
            {
                if (int.TryParse(userIdClaim.Value, out var parsedUserId))
                {
                    userId = parsedUserId;
                    _logger.LogDebug($"UserId from token: {userId}");
                }
                else
                {
                    _logger.LogWarning($"Failed to parse UserId from claim: {userIdClaim.Value}");
                }
            }
            else
            {
                _logger.LogDebug($"NameIdentifier claim not found. Available claims: {string.Join(", ", context.User.Claims.Select(c => c.Type))}");
            }
        }
        else
        {
            // Only log for non-anonymous endpoints
            var endpoint = context.GetEndpoint();
            var allowAnonymous = endpoint?.Metadata?.GetMetadata<Microsoft.AspNetCore.Authorization.IAllowAnonymous>() != null;

            if (!allowAnonymous && !context.Request.Path.StartsWithSegments("/health"))
            {
                _logger.LogDebug("Unauthenticated request to protected endpoint");
            }
        }

        // Fallback to X-User-Id header (for development/testing)
        if (!userId.HasValue && context.Request.Headers.TryGetValue("X-User-Id", out var headerUserId))
        {
            if (int.TryParse(headerUserId.FirstOrDefault(), out var parsedUserId))
            {
                userId = parsedUserId;
                _logger.LogDebug($"UserId from header: {userId}");
            }
        }

        if (userId.HasValue)
        {
            context.Items["UserId"] = userId.Value;
        }

        await _next(context);
    }
}

public static class UserContextMiddlewareExtensions
{
    public static IApplicationBuilder UseUserContext(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<UserContextMiddleware>();
    }
}
