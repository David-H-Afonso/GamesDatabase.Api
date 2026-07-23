using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using GamesDatabase.Api.Application.Mapping;
using GamesDatabase.Api.Authentication;
using GamesDatabase.Api.Common;

namespace GamesDatabase.Api.Controllers;

[ApiController]
[Authorize]
public abstract class BaseApiController : ControllerBase
{
    protected int? CurrentUserId => HttpContext.GetUserId();

    protected int GetCurrentUserIdOrDefault(int defaultUserId = 1)
    {
        return HttpContext.GetUserIdOrDefault(defaultUserId);
    }

    protected ActionResult RequireUserId()
    {
        if (!CurrentUserId.HasValue)
        {
            return Unauthorized(new { message = "User authentication required. Please provide X-User-Id header or valid JWT token" });
        }
        return Ok();
    }

    protected bool HasRequiredIntegrationScope(string scope)
    {
        var integrationIdentity = User.Identities.FirstOrDefault(identity =>
            identity.IsAuthenticated &&
            string.Equals(
                identity.AuthenticationType,
                HouseholdAccessTokenDefaults.AuthenticationScheme,
                StringComparison.Ordinal));

        return integrationIdentity is null ||
            integrationIdentity.HasClaim(HouseholdAccessTokenDefaults.ScopeClaim, scope);
    }
}
