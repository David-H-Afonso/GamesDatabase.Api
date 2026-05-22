using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using GamesDatabase.Api.Contracts;
using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Common;

namespace GamesDatabase.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class GamePlatformsController : BaseApiController
{
    private readonly ICatalogService _catalogService;

    public GamePlatformsController(ICatalogService catalogService)
    {
        _catalogService = catalogService;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<GamePlatformDto>>> GetGamePlatforms([FromQuery] QueryParameters parameters)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var result = await _catalogService.GetPlatformsAsync(parameters, userId);
        return Ok(result);
    }

    [HttpGet("active")]
    public async Task<ActionResult<IEnumerable<GamePlatformDto>>> GetActiveGamePlatforms()
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var result = await _catalogService.GetActivePlatformsAsync(userId);
        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<GamePlatformDto>> GetGamePlatform(int id)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var result = await _catalogService.GetPlatformByIdAsync(id, userId);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> PutGamePlatform(int id, GamePlatformUpdateDto updateDto)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var result = await _catalogService.UpdatePlatformAsync(id, updateDto, userId);
        return ToCatalogActionResult(result);
    }

    [HttpPost]
    public async Task<ActionResult<GamePlatformDto>> PostGamePlatform(GamePlatformCreateDto createDto)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var result = await _catalogService.CreatePlatformAsync(createDto, userId);
        if (result.Conflict) return Conflict(new { message = result.Error });
        if (!result.Success) return BadRequest(new { message = result.Error });
        return CreatedAtAction("GetGamePlatform", new { id = result.Data!.Id }, result.Data);
    }

    [HttpPost("reorder")]
    public async Task<IActionResult> ReorderPlatforms([FromBody] ReorderStatusesDto dto)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var result = await _catalogService.ReorderPlatformsAsync(dto, userId);
        if (result.Success) return Ok(new { message = "Platforms reordered successfully" });
        return ToCatalogActionResult(result);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteGamePlatform(int id)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var result = await _catalogService.DeletePlatformAsync(id, userId);
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
