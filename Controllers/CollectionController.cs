using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GamesDatabase.Api.Controllers;

[ApiController, Authorize, Route("api/collection")]
public sealed class CollectionController(ICollectionService collection, IGameDiscoveryService discovery) : BaseApiController
{
    [HttpGet("random-game")]
    public async Task<IActionResult> RandomGame(CancellationToken cancellationToken)
    {
        if (!CurrentUserId.HasValue) return Unauthorized();
        Response.Headers.CacheControl = "no-store";
        var game = await discovery.RandomAsync(CurrentUserId.Value, cancellationToken);
        return game is null ? NotFound(new { code = "emptyLibrary" }) : Ok(game);
    }

    [HttpPost("random-game")]
    public async Task<IActionResult> RandomDisplayed(DisplayedRandomGameRequest request, CancellationToken cancellationToken)
    {
        if (!CurrentUserId.HasValue) return Unauthorized();
        Response.Headers.CacheControl = "no-store";
        var result = await discovery.RandomDisplayedAsync(CurrentUserId.Value, request.GameIds, cancellationToken);
        return result.Success ? Ok(result.Data) : result.NotFound ? NotFound(new { code = "emptySelection" }) : result.Conflict ? Conflict(new { code = "displayedScopeChanged" }) : BadRequest(new { code = "invalidDisplayedScope" });
    }

    [HttpPost("recommendations/preview")]
    public async Task<IActionResult> Recommendations(RecommendationFiltersDto filters, [FromQuery] int count = 6, CancellationToken cancellationToken = default)
    {
        if (!CurrentUserId.HasValue) return Unauthorized();
        Response.Headers.CacheControl = "no-store";
        if (count is < 1 or > 24) return BadRequest(new { code = "invalidRecommendationCount" });
        try { return Ok(await discovery.DailyAsync(CurrentUserId.Value, filters, count, cancellationToken)); }
        catch (ArgumentException) { return BadRequest(new { code = "invalidRecommendationFilters" }); }
    }

    [HttpGet("statistics")]
    public async Task<IActionResult> Statistics([FromQuery] int? year, [FromQuery] int? month, [FromQuery] string dateKind = "finished", [FromQuery] bool allRanking = false)
    {
        if (!CurrentUserId.HasValue) return Unauthorized();
        if (year is < 1900 or > 2100 || month is < 1 or > 12 || dateKind is not ("started" or "finished")) return BadRequest(new { error = "Invalid statistics period." });
        Response.Headers.CacheControl = "no-store";
        return Ok(await collection.GetStatisticsAsync(CurrentUserId.Value, year ?? DateTime.UtcNow.Year, month, dateKind, allRanking));
    }

    [HttpGet("games")]
    public async Task<IActionResult> Games([FromQuery] string metric = "all", [FromQuery] int? year = null, [FromQuery] int? month = null, [FromQuery] int? value = null, [FromQuery] string? developer = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 28, [FromQuery] string? search = null)
    {
        if (!CurrentUserId.HasValue) return Unauthorized();
        if (year is < 1900 or > 2100 || month is < 1 or > 12 || developer?.Length > 150 || search?.Length > 250) return BadRequest(new { error = "Invalid collection query." });
        Response.Headers.CacheControl = "no-store";
        try { return Ok(await collection.GetMetricGamesAsync(CurrentUserId.Value, metric, year ?? DateTime.UtcNow.Year, month, value, developer, page, pageSize, search)); }
        catch (ArgumentException) { return BadRequest(new { error = "Unknown collection metric." }); }
    }

    [HttpGet("goty")]
    public async Task<IActionResult> Goty()
    {
        if (!CurrentUserId.HasValue) return Unauthorized();
        Response.Headers.CacheControl = "no-store";
        return Ok(await collection.GetGotyAsync(CurrentUserId.Value));
    }

    [HttpPut("goty/{year:int}")]
    public async Task<IActionResult> SaveGoty(int year, GotySelectionRequest request)
    {
        if (!CurrentUserId.HasValue) return Unauthorized();
        var result = await collection.SaveGotyAsync(year, request, CurrentUserId.Value);
        return result.Success ? Ok(result.Data) : result.Conflict ? Conflict(new { error = result.Error }) : result.NotFound ? NotFound(new { error = result.Error }) : BadRequest(new { error = result.Error });
    }
}

[ApiController, Authorize, Route("api/home")]
public sealed class HomeController(ICollectionService collection) : BaseApiController
{
    [HttpGet("rows/{id}/games")]
    public async Task<IActionResult> Games(string id, [FromQuery] int page = 1, [FromQuery] int pageSize = 28, [FromQuery] string? search = null)
    {
        if (!CurrentUserId.HasValue) return Unauthorized();
        if (id.Length > 80 || search?.Length > 250) return BadRequest(new { error = "Invalid row query." });
        Response.Headers.CacheControl = "no-store";
        var result = await collection.GetHomeRowGamesAsync(id, CurrentUserId.Value, page, pageSize, search);
        return result is null ? NotFound(new { error = "Source unavailable." }) : Ok(result);
    }

    [HttpGet("config")]
    public async Task<IActionResult> Config()
    {
        if (!CurrentUserId.HasValue) return Unauthorized();
        Response.Headers.CacheControl = "no-store";
        return Ok(await collection.GetHomeAsync(CurrentUserId.Value));
    }

    [HttpPut("config")]
    public async Task<IActionResult> SaveConfig(HomeConfigDto request)
    {
        if (!CurrentUserId.HasValue) return Unauthorized();
        var result = await collection.SaveHomeAsync(request, CurrentUserId.Value);
        return result.Success ? Ok(result.Data) : result.Conflict ? Conflict(new { error = result.Error }) : BadRequest(new { error = result.Error });
    }

    [HttpPost("rows/resolve")]
    public async Task<IActionResult> Resolve(HomeResolveRequest request)
    {
        if (!CurrentUserId.HasValue) return Unauthorized();
        Response.Headers.CacheControl = "no-store";
        var result = await collection.ResolveHomeAsync(request, CurrentUserId.Value);
        return result.Success ? Ok(result.Data) : BadRequest(new { error = result.Error });
    }
}
