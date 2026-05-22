using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using GamesDatabase.Api.Contracts;
using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Common;

namespace GamesDatabase.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class GameReplayTypesController : BaseApiController
{
    private readonly ICatalogService _catalogService;

    public GameReplayTypesController(ICatalogService catalogService)
    {
        _catalogService = catalogService;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<GameReplayTypeDto>>> GetGameReplayTypes([FromQuery] QueryParameters parameters)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var result = await _catalogService.GetReplayTypesAsync(parameters, userId);
        return Ok(result);
    }

    [HttpGet("active")]
    public async Task<ActionResult<IEnumerable<GameReplayTypeDto>>> GetActiveGameReplayTypes()
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var result = await _catalogService.GetActiveReplayTypesAsync(userId);
        return Ok(result);
    }

    [HttpGet("special")]
    public async Task<ActionResult<GameReplayTypeDto>> GetSpecialGameReplayType()
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var result = await _catalogService.GetOrCreateSpecialReplayTypeAsync(userId);
        return Ok(result.Data);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<GameReplayTypeDto>> GetGameReplayType(int id)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var result = await _catalogService.GetReplayTypeByIdAsync(id, userId);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<GameReplayTypeDto>> PostGameReplayType(GameReplayTypeCreateDto createDto)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var result = await _catalogService.CreateReplayTypeAsync(createDto, userId);
        if (result.Conflict) return Conflict(new { message = result.Error });
        if (!result.Success) return BadRequest(new { message = result.Error });
        return CreatedAtAction(nameof(GetGameReplayType), new { id = result.Data!.Id }, result.Data);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> PutGameReplayType(int id, GameReplayTypeUpdateDto updateDto)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var result = await _catalogService.UpdateReplayTypeAsync(id, updateDto, userId);
        return ToCatalogActionResult(result);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteGameReplayType(int id)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var result = await _catalogService.DeleteReplayTypeAsync(id, userId);
        if (result.Success) return NoContent();
        return ToCatalogActionResult(result);
    }

    [HttpPost("reorder")]
    public async Task<IActionResult> ReorderGameReplayTypes([FromBody] ReorderReplayTypesDto reorderDto)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var result = await _catalogService.ReorderReplayTypesAsync(reorderDto, userId);
        if (result.Success) return NoContent();
        return ToCatalogActionResult(result);
    }

    private ActionResult ToCatalogActionResult(CatalogServiceResult result)
    {
        if (result.Success) return NoContent();
        if (result.NotFound) return NotFound(new { message = result.Error });
        if (result.Conflict) return Conflict(new { message = result.Error });
        if (result.StatusCode == 500) return StatusCode(500, new { message = result.Error });
        return BadRequest(new { message = result.Error });
    }
}
