using System.Security.Claims;
using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Authentication;
using GamesDatabase.Api.Contracts;
using GamesDatabase.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace GamesDatabase.Api.Controllers;

[ApiController]
[Route("api/integrations/household/v1")]
[EnableRateLimiting("integration")]
public sealed class HouseholdIntegrationsController : ControllerBase
{
    private readonly IHouseholdIntegrationService _service;
    private readonly GamesDbContext _context;

    public HouseholdIntegrationsController(
        IHouseholdIntegrationService service,
        GamesDbContext context)
    {
        _service = service;
        _context = context;
    }

    [HttpPost("authorize")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Authorize(HouseholdAuthorizeRequest request)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var result = await _service.AuthorizeAsync(userId, request);
        if (result.Success)
        {
            return Ok(new HouseholdAuthorizeResponse { RedirectUrl = result.RedirectUrl! });
        }

        var body = new
        {
            error = result.Error,
            errorDescription = result.ErrorDescription,
            redirectUrl = result.CanRedirect ? result.RedirectUrl : null
        };
        return BadRequest(body);
    }

    [HttpPost("token")]
    [AllowAnonymous]
    public async Task<IActionResult> Token(HouseholdTokenRequest request)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";

        var result = await _service.ExchangeTokenAsync(request);
        return result.Success
            ? Ok(result.Response)
            : BadRequest(new OAuthErrorResponse
            {
                Error = result.Error!,
                ErrorDescription = result.ErrorDescription
            });
    }

    [HttpPost("revoke")]
    [AllowAnonymous]
    public async Task<IActionResult> Revoke(HouseholdRevokeRequest request)
    {
        await _service.RevokeAsync(request.Token);
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize(AuthenticationSchemes = HouseholdAccessTokenDefaults.AuthenticationScheme)]
    public async Task<ActionResult<HouseholdMeResponse>> Me()
    {
        var connectionIdValue = User.FindFirstValue(HouseholdAccessTokenDefaults.ConnectionIdClaim);
        if (!Guid.TryParse(connectionIdValue, out var connectionId))
        {
            return Unauthorized();
        }

        var connection = await _context.HouseholdConnections
            .AsNoTracking()
            .Include(item => item.User)
            .FirstOrDefaultAsync(item => item.Id == connectionId);
        if (connection is null)
        {
            return Unauthorized();
        }

        return Ok(new HouseholdMeResponse
        {
            ConnectionId = connection.Id,
            ClientId = connection.ClientId,
            Scope = connection.GrantedScopes,
            Account = new HouseholdAccountDto
            {
                Id = connection.AccountId,
                DisplayName = connection.User.Username
            }
        });
    }

    private bool TryGetUserId(out int userId) =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
}
