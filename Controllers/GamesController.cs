using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using GamesDatabase.Api.Contracts;
using GamesDatabase.Api.Common;
using GamesDatabase.Api.Application.Interfaces;
using System.Text.Json;
using GamesDatabase.Api.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace GamesDatabase.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class GamesController : BaseApiController
{
    private readonly IGameService _gameService;

    public GamesController(IGameService gameService)
    {
        _gameService = gameService;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<GameDto>>> GetGames([FromQuery] GameQueryParameters parameters)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var result = await _gameService.GetGamesAsync(parameters, userId);

        if (!result.Success)
            return BadRequest(result.Error);

        return Ok(result.Data);
    }

    /// <summary>
    /// Small, ownership-scoped read model for server-to-server integrations.
    /// </summary>
    [HttpGet("summary")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + HouseholdAccessTokenDefaults.AuthenticationScheme)]
    public async Task<ActionResult<GameSummaryDto>> GetSummary()
    {
        if (!CurrentUserId.HasValue)
            return Unauthorized(new { message = "User authentication required." });

        if (!HasRequiredIntegrationScope("games.read"))
            return Forbid();

        var userId = CurrentUserId.Value;
        return Ok(await _gameService.GetSummaryAsync(userId));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<GameDto>> GetGame(int id)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var gameDto = await _gameService.GetGameByIdAsync(id, userId);

        if (gameDto == null)
            return NotFound();

        return Ok(gameDto);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> PutGame(int id, [FromBody] JsonElement gameDto)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var result = await _gameService.UpdateGameAsync(id, gameDto, userId);

        if (result.NotFound)
            return NotFound();

        if (!result.Success)
            return BadRequest(new { error = result.Error });

        return NoContent();
    }

    /// <summary>
    /// Updates only the status field and records the change in game history.
    /// </summary>
    [HttpPatch("{id}/status")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + HouseholdAccessTokenDefaults.AuthenticationScheme)]
    public async Task<ActionResult<GameDto>> PatchStatus(int id, [FromBody] GameStatusPatchDto request)
    {
        if (!CurrentUserId.HasValue)
            return Unauthorized(new { message = "User authentication required." });

        if (!HasRequiredIntegrationScope("games.status.write"))
            return Forbid();

        var userId = CurrentUserId.Value;
        var result = await _gameService.UpdateGameStatusAsync(id, request.StatusId, userId);

        if (result.NotFound)
            return NotFound();

        if (!result.Success)
            return BadRequest(new { error = result.Error });

        return Ok(result.Data);
    }

    private bool HasRequiredIntegrationScope(string scope) =>
        !User.HasClaim(HouseholdAccessTokenDefaults.IntegrationClaim, "true") ||
        User.HasClaim(HouseholdAccessTokenDefaults.ScopeClaim, scope);

    [HttpPost]
    public async Task<ActionResult<GameDto>> PostGame(GameCreateDto gameDto)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var result = await _gameService.CreateGameAsync(gameDto, userId);

        if (!result.Success)
            return BadRequest(new { message = result.Error });

        return CreatedAtAction("GetGame", new { id = result.Data!.Id }, result.Data);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteGame(int id)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var deleted = await _gameService.DeleteGameAsync(id, userId);

        if (!deleted)
            return NotFound();

        return NoContent();
    }

    [HttpPatch("bulk")]
    public async Task<ActionResult<BulkUpdateResult>> BulkUpdateGames([FromBody] BulkUpdateGameDto bulkUpdate)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var result = await _gameService.BulkUpdateGamesAsync(bulkUpdate, userId);

        if (result.NotFound)
            return NotFound(result.Error);

        if (!result.Success)
            return BadRequest(result.Error);

        return Ok(result.Data);
    }
}
