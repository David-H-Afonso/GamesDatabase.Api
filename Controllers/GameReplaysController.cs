using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using GamesDatabase.Api.Contracts;
using GamesDatabase.Api.Application.Interfaces;

namespace GamesDatabase.Api.Controllers;

[ApiController]
[Route("api/games/{gameId}/replays")]
[Authorize]
public class GameReplaysController : BaseApiController
{
    private readonly IGameReplayService _replays;

    public GameReplaysController(IGameReplayService replays)
    {
        _replays = replays;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<GameReplayDto>>> GetReplaysForGame(int gameId)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var result = await _replays.GetReplaysForGameAsync(userId, gameId);
        if (result == null) return NotFound(new { message = "Juego no encontrado" });
        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<GameReplayDto>> GetGameReplay(int gameId, int id)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var result = await _replays.GetGameReplayAsync(userId, gameId, id);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<GameReplayDto>> PostGameReplay(int gameId, GameReplayCreateDto createDto)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var (dto, error) = await _replays.CreateReplayAsync(userId, gameId, createDto);
        if (dto == null) return BadRequest(new { message = error });
        return CreatedAtAction(nameof(GetGameReplay), new { gameId, id = dto.Id }, dto);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> PutGameReplay(int gameId, int id, GameReplayUpdateDto updateDto)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var (success, error) = await _replays.UpdateReplayAsync(userId, gameId, id, updateDto);
        if (!success && error != null) return BadRequest(new { message = error });
        if (!success) return NotFound();
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteGameReplay(int gameId, int id)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var deleted = await _replays.DeleteReplayAsync(userId, gameId, id);
        if (!deleted) return NotFound();
        return NoContent();
    }
}
