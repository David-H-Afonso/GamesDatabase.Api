using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using GamesDatabase.Api.Contracts;
using GamesDatabase.Api.Common;
using GamesDatabase.Api.Application.Interfaces;

namespace GamesDatabase.Api.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public class GameHistoryController : BaseApiController
{
    private readonly IGameHistoryService _history;

    public GameHistoryController(IGameHistoryService history)
    {
        _history = history;
    }

    [HttpGet("games/{gameId}/history")]
    public async Task<ActionResult<PagedResult<GameHistoryEntryDto>>> GetGameHistory(
        int gameId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var result = await _history.GetGameHistoryAsync(userId, gameId, page, pageSize);
        return Ok(result);
    }

    [HttpGet("games/history")]
    public async Task<ActionResult<PagedResult<GameHistoryEntryDto>>> GetAllHistory(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? actionType = null,
        [FromQuery] string? field = null,
        [FromQuery] int? gameId = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] string? search = null)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var result = await _history.GetAllHistoryAsync(userId, page, pageSize, actionType, field, gameId, from, to, search);
        return Ok(result);
    }

    [HttpDelete("games/{gameId}/history/{entryId}")]
    public async Task<IActionResult> DeleteHistoryEntry(int gameId, int entryId)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var deleted = await _history.DeleteHistoryEntryAsync(userId, gameId, entryId);
        if (!deleted) return NotFound();
        return NoContent();
    }

    [HttpDelete("games/{gameId}/history")]
    public async Task<IActionResult> DeleteAllGameHistory(int gameId)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var deleted = await _history.DeleteAllGameHistoryAsync(userId, gameId);
        if (!deleted) return NotFound(new { message = "No hay entradas de historial para este juego" });
        return NoContent();
    }

    [HttpGet("admin/history")]
    public async Task<ActionResult<PagedResult<GameHistoryEntryDto>>> GetAdminHistory(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] int? userId = null,
        [FromQuery] string? actionType = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] string? field = null,
        [FromQuery] string? search = null)
    {
        var currentUserId = GetCurrentUserIdOrDefault(1);
        var result = await _history.GetAdminHistoryAsync(currentUserId, page, pageSize, userId, actionType, from, to, field, search);
        if (result == null) return Forbid();
        return Ok(result);
    }
}
