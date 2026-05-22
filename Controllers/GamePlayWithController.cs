using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using GamesDatabase.Api.Contracts;
using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Common;

namespace GamesDatabase.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GamePlayWithController : BaseApiController
{
    private readonly ICatalogService _catalogService;

    public GamePlayWithController(ICatalogService catalogService)
    {
        _catalogService = catalogService;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<GamePlayWithDto>>> GetGamePlayWiths([FromQuery] QueryParameters parameters)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var result = await _catalogService.GetPlayWithsAsync(parameters, userId);
        return Ok(result);
    }

    [HttpGet("active")]
    public async Task<ActionResult<IEnumerable<GamePlayWithDto>>> GetActiveGamePlayWiths()
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var result = await _catalogService.GetActivePlayWithsAsync(userId);
        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<GamePlayWithDto>> GetGamePlayWith(int id)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var result = await _catalogService.GetPlayWithByIdAsync(id, userId);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> PutGamePlayWith(int id, GamePlayWithUpdateDto updateDto)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var result = await _catalogService.UpdatePlayWithAsync(id, updateDto, userId);
        return ToCatalogActionResult(result);
    }

    [HttpPost]
    public async Task<ActionResult<GamePlayWithDto>> PostGamePlayWith(GamePlayWithCreateDto createDto)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var result = await _catalogService.CreatePlayWithAsync(createDto, userId);
        if (result.Conflict) return Conflict(new { message = result.Error });
        if (!result.Success) return BadRequest(new { message = result.Error });
        return CreatedAtAction("GetGamePlayWith", new { id = result.Data!.Id }, result.Data);
    }

    [HttpPost("reorder")]
    public async Task<IActionResult> ReorderPlayWith([FromBody] ReorderStatusesDto dto)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var result = await _catalogService.ReorderPlayWithsAsync(dto, userId);
        if (result.Success) return Ok(new { message = "Play-with options reordered successfully" });
        return ToCatalogActionResult(result);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteGamePlayWith(int id)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var result = await _catalogService.DeletePlayWithAsync(id, userId);
        if (result.Success) return NoContent();
        if (result.ErrorData != null) return result.NotFound ? NotFound(result.ErrorData) : BadRequest(result.ErrorData);
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
