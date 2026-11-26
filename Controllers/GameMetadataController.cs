using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace GamesDatabase.Api.Controllers;

[ApiController, Authorize, Route("api/games/{gameId:int}/metadata")]
public sealed class GameMetadataController(IGameMetadataService metadata) : BaseApiController
{
    [HttpPost("preview")]
    [EnableRateLimiting("metadata")]
    public async Task<IActionResult> Preview(int gameId, GameMetadataLookupRequest request, CancellationToken cancellationToken)
    {
        if (!CurrentUserId.HasValue) return Unauthorized();
        Response.Headers.CacheControl = "no-store";
        var result = await metadata.PreviewAsync(gameId, CurrentUserId.Value, request, cancellationToken);
        return result.Success ? Ok(result.Data) : result.NotFound ? NotFound(new { code = "gameUnavailable" }) : BadRequest(new { code = "invalidMetadataLookup" });
    }
    [HttpPost("apply")]
    public async Task<IActionResult> Apply(int gameId, GameMetadataApplyRequest request, CancellationToken cancellationToken)
    {
        if (!CurrentUserId.HasValue) return Unauthorized();
        Response.Headers.CacheControl = "no-store";
        var result = await metadata.ApplyAsync(gameId, CurrentUserId.Value, request, cancellationToken);
        return result.Success ? Ok(result.Data) : result.StatusCode == 403 ? Forbid() : result.Conflict ? Conflict(new { code = "metadataConflict" }) : result.NotFound ? NotFound(new { code = "gameUnavailable" }) : BadRequest(new { code = "invalidMetadataSelection" });
    }
}
