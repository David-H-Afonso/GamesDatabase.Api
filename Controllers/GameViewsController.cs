using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Text.Json;
using GamesDatabase.Api.Contracts;
using GamesDatabase.Api.Application.Interfaces;

namespace GamesDatabase.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GameViewsController : BaseApiController
{
    private readonly IGameViewService _gameViewService;

    public GameViewsController(IGameViewService gameViewService)
    {
        _gameViewService = gameViewService;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<GameViewSummaryDto>>> GetGameViews()
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var result = await _gameViewService.GetViewsAsync(userId);
        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<GameViewDto>> GetGameView(int id)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var result = await _gameViewService.GetViewByIdAsync(id, userId);
        if (result == null) return NotFound($"Vista con ID {id} no encontrada.");
        return Ok(result);
    }

    [HttpGet("by-name/{name}")]
    public async Task<ActionResult<GameViewDto>> GetGameViewByName(string name)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var result = await _gameViewService.GetViewByNameAsync(name, userId);
        if (result == null) return NotFound($"Vista con nombre '{name}' no encontrada.");
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<GameViewDto>> CreateGameView(GameViewCreateDto createDto)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var result = await _gameViewService.CreateViewAsync(createDto, userId);
        if (result.Success) return CreatedAtAction(nameof(GetGameView), new { id = result.Data!.Id }, result.Data);
        if (result.StatusCode == 500) return StatusCode(500, result.Error);
        return BadRequest(result.Error);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<GameViewDto>> UpdateGameView(int id, GameViewUpdateDto updateDto)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var result = await _gameViewService.UpdateViewAsync(id, updateDto, userId);
        if (result.Success) return Ok(result.Data);
        if (result.NotFound) return NotFound(result.Error);
        if (result.StatusCode == 500) return StatusCode(500, result.Error);
        return BadRequest(result.Error);
    }

    [HttpPost("reorder")]
    public async Task<IActionResult> ReorderGameViews([FromBody] ReorderStatusesDto dto)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var result = await _gameViewService.ReorderViewsAsync(dto, userId);
        if (result.Success) return Ok(new { message = "Views reordered successfully" });
        if (result.NotFound) return NotFound(new { message = result.Error });
        if (result.StatusCode == 500) return StatusCode(500, new { message = result.Error });
        return BadRequest(new { message = result.Error });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteGameView(int id)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var result = await _gameViewService.DeleteViewAsync(id, userId);
        if (result.Success) return NoContent();
        if (result.NotFound) return NotFound(result.Error);
        if (result.StatusCode == 500) return StatusCode(500, result.Error);
        return BadRequest(result.Error);
    }

    [HttpPost("{id}/duplicate")]
    public async Task<ActionResult<GameViewDto>> DuplicateGameView(int id, [FromBody] string newName)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var result = await _gameViewService.DuplicateViewAsync(id, newName, userId);
        if (result.Success) return CreatedAtAction(nameof(GetGameView), new { id = result.Data!.Id }, result.Data);
        if (result.NotFound) return NotFound(result.Error);
        if (result.StatusCode == 500) return StatusCode(500, result.Error);
        return BadRequest(result.Error);
    }

    [HttpPut("{id}/configuration")]
    public async Task<ActionResult<GameViewDto>> UpdateGameViewConfiguration(int id, [FromBody] JsonElement body)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var result = await _gameViewService.UpdateViewConfigurationAsync(id, body, userId);
        if (result.Success) return Ok(result.Data);
        if (result.NotFound) return NotFound(result.Error);
        if (result.StatusCode == 500) return StatusCode(500, result.Error);
        return BadRequest(result.Error);
    }
}
