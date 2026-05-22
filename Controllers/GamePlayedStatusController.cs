using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using GamesDatabase.Api.Contracts;
using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Common;

namespace GamesDatabase.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GamePlayedStatusController : BaseApiController
{
    private readonly ICatalogService _catalogService;

    public GamePlayedStatusController(ICatalogService catalogService)
    {
        _catalogService = catalogService;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<GamePlayedStatusDto>>> GetGamePlayedStatuses([FromQuery] QueryParameters parameters)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var result = await _catalogService.GetPlayedStatusesAsync(parameters, userId);
        return Ok(result);
    }

    [HttpGet("active")]
    public async Task<ActionResult<IEnumerable<GamePlayedStatusDto>>> GetActiveGamePlayedStatuses()
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var result = await _catalogService.GetActivePlayedStatusesAsync(userId);
        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<GamePlayedStatusDto>> GetGamePlayedStatus(int id)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var result = await _catalogService.GetPlayedStatusByIdAsync(id, userId);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> PutGamePlayedStatus(int id, GamePlayedStatusUpdateDto updateDto)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var result = await _catalogService.UpdatePlayedStatusAsync(id, updateDto, userId);
        return ToCatalogActionResult(result);
    }

    [HttpPost]
    public async Task<ActionResult<GamePlayedStatusDto>> PostGamePlayedStatus(GamePlayedStatusCreateDto createDto)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var result = await _catalogService.CreatePlayedStatusAsync(createDto, userId);
        if (result.Conflict) return Conflict(new { message = result.Error });
        if (!result.Success) return BadRequest(new { message = result.Error });
        return CreatedAtAction("GetGamePlayedStatus", new { id = result.Data!.Id }, result.Data);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteGamePlayedStatus(int id)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var result = await _catalogService.DeletePlayedStatusAsync(id, userId);
        if (result.Success) return NoContent();
        if (result.ErrorData != null) return result.NotFound ? NotFound(result.ErrorData) : BadRequest(result.ErrorData);
        return ToCatalogActionResult(result);
    }

    [HttpPost("reorder")]
    public async Task<IActionResult> ReorderPlayedStatuses([FromBody] ReorderStatusesDto dto)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var result = await _catalogService.ReorderPlayedStatusesAsync(dto, userId);
        if (result.Success) return Ok(new { message = "Played statuses reordered successfully" });
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
