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
            _logger.LogInformation("Usuario autenticado detectado");
            var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim != null)
            {
                _logger.LogInformation($"Claim encontrado: {userIdClaim.Value}");
                if (int.TryParse(userIdClaim.Value, out var parsedUserId))
                {
                    userId = parsedUserId;
                    _logger.LogInformation($"UserId parseado: {userId}");
                }
                else
                {
                    _logger.LogWarning($"No se pudo parsear el userId: {userIdClaim.Value}");
                }
            }
            else
            {
                _logger.LogWarning("No se encontró el claim NameIdentifier");
                _logger.LogInformation($"Claims disponibles: {string.Join(", ", context.User.Claims.Select(c => $"{c.Type}={c.Value}"))}");
            }
        }
        else
        {
            _logger.LogWarning("Usuario no autenticado");
        }

        if (!userId.HasValue && context.Request.Headers.TryGetValue("X-User-Id", out var headerUserId))
        {
            if (int.TryParse(headerUserId.FirstOrDefault(), out var parsedUserId))
            {
                userId = parsedUserId;
                _logger.LogInformation($"UserId obtenido del header: {userId}");
            }
        }

        if (userId.HasValue)
        {
            context.Items["UserId"] = userId.Value;
            _logger.LogInformation($"UserId guardado en context: {userId}");
        }
        else
        {
            _logger.LogWarning("No se pudo obtener el UserId");
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
