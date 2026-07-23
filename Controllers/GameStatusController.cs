using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using GamesDatabase.Api.Contracts;
using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Authentication;
using GamesDatabase.Api.Common;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace GamesDatabase.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class GameStatusController : BaseApiController
{
    private readonly ICatalogService _catalogService;

    public GameStatusController(ICatalogService catalogService)
    {
        _catalogService = catalogService;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<GameStatusDto>>> GetGameStatuses([FromQuery] QueryParameters parameters)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var result = await _catalogService.GetStatusesAsync(parameters, userId);
        return Ok(result);
    }

    [HttpGet("active")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + HouseholdAccessTokenDefaults.AuthenticationScheme)]
    public async Task<ActionResult<IEnumerable<GameStatusDto>>> GetActiveGameStatuses()
    {
        if (!CurrentUserId.HasValue)
            return Unauthorized(new { message = "User authentication required." });

        if (!HasRequiredIntegrationScope("games.read"))
            return Forbid();

        var userId = CurrentUserId.Value;
        var result = await _catalogService.GetActiveStatusesAsync(userId);
        return Ok(result);
    }

    [HttpGet("ordered")]
    public async Task<ActionResult<IEnumerable<object>>> GetOrderedStatuses()
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var result = await _catalogService.GetOrderedStatusesAsync(userId);
        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<GameStatusDto>> GetGameStatus(int id)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var result = await _catalogService.GetStatusByIdAsync(id, userId);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> PutGameStatus(int id, GameStatusUpdateDto updateDto)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var result = await _catalogService.UpdateStatusAsync(id, updateDto, userId);
        return ToCatalogActionResult(result);
    }

    [HttpPost]
    public async Task<ActionResult<GameStatusDto>> PostGameStatus(GameStatusCreateDto createDto)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var result = await _catalogService.CreateStatusAsync(createDto, userId);
        if (result.Conflict) return Conflict(new { message = result.Error });
        if (!result.Success) return BadRequest(new { message = result.Error });
        return CreatedAtAction("GetGameStatus", new { id = result.Data!.Id }, result.Data);
    }

    [HttpPost("reorder")]
    public async Task<IActionResult> ReorderStatuses([FromBody] ReorderStatusesDto dto)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var result = await _catalogService.ReorderStatusesAsync(dto, userId);
        if (result.Success) return Ok(new { message = "Statuses reordered successfully" });
        return ToCatalogActionResult(result);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteGameStatus(int id)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var result = await _catalogService.DeleteStatusAsync(id, userId);
        if (result.Success) return NoContent();
        if (result.ErrorData != null) return result.NotFound ? NotFound(result.ErrorData) : BadRequest(result.ErrorData);
        return ToCatalogActionResult(result);
    }

    [HttpGet("special")]
    public async Task<ActionResult<IEnumerable<SpecialStatusDto>>> GetSpecialStatuses()
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var result = await _catalogService.GetSpecialStatusesAsync(userId);
        return Ok(result);
    }

    [HttpPost("reassign-special")]
    public async Task<IActionResult> ReassignSpecialStatus(ReassignDefaultStatusDto reassignDto)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var result = await _catalogService.ReassignSpecialStatusAsync(reassignDto, userId);
        if (result.Success) return Ok(result.Data);
        if (result.StatusCode == 500) return StatusCode(500, new { message = result.Error });
        if (result.NotFound) return NotFound(new { message = result.Error });
        return BadRequest(new { message = result.Error });
    }

    [HttpDelete("special/{id}")]
    public async Task<IActionResult> DeleteSpecialStatus(int id)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var result = await _catalogService.DeleteSpecialStatusAsync(id, userId);
        if (result.Success) return Ok(result.Data);
        if (result.ErrorData != null) return BadRequest(result.ErrorData);
        if (result.NotFound) return NotFound(new { message = result.Error });
        return BadRequest(new { message = result.Error });
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
