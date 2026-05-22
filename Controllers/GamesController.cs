using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using GamesDatabase.Api.Contracts;
using GamesDatabase.Api.Common;
using GamesDatabase.Api.Application.Interfaces;
using System.Text.Json;

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
