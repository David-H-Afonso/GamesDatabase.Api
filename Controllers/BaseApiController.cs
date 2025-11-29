using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using GamesDatabase.Api.Helpers;

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
}
