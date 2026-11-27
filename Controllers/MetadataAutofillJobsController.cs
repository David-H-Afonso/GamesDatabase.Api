using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace GamesDatabase.Api.Controllers;

[ApiController, Authorize, Route("api/metadata-autofill/jobs"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class MetadataAutofillJobsController(IMetadataAutofillJobService jobs) : BaseApiController
{
    private IActionResult Result<T>(CatalogServiceResult<T> result, bool accepted = false) => result.Success ? accepted ? Accepted(result.Data) : Ok(result.Data)
        : StatusCode(result.NotFound ? 404 : result.Conflict ? 409 : result.StatusCode >= 400 ? result.StatusCode : 400, new { code = result.Error ?? "metadataJobFailed" });
    [HttpPost, EnableRateLimiting("metadata")]
    public async Task<IActionResult> Start(MetadataAutofillStartRequest request, CancellationToken token)
    {
        Response.Headers.CacheControl = "no-store";
        return !CurrentUserId.HasValue ? Unauthorized() : Result(await jobs.StartAsync(CurrentUserId.Value, request, token), true);
    }
    [HttpGet("current")]
    public async Task<IActionResult> Current([FromQuery] bool includeItems = true, CancellationToken token = default)
    {
        Response.Headers.CacheControl = "no-store";
        return !CurrentUserId.HasValue ? Unauthorized() : new JsonResult(await jobs.CurrentAsync(CurrentUserId.Value, includeItems, token)) { StatusCode = 200 };
    }
    [HttpGet]
    public async Task<IActionResult> History([FromQuery] int skip = 0, [FromQuery] int take = 10, CancellationToken token = default) => !CurrentUserId.HasValue ? Unauthorized() : Ok(await jobs.HistoryAsync(CurrentUserId.Value, skip, take, token));
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, [FromQuery] bool includeItems = true, CancellationToken token = default)
    {
        Response.Headers.CacheControl = "no-store";
        if (!CurrentUserId.HasValue) return Unauthorized();
        var result = await jobs.GetAsync(CurrentUserId.Value, id, includeItems, token);
        return result is null ? NotFound(new { code = "metadataJobUnavailable" }) : Ok(result);
    }
    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken token) => !CurrentUserId.HasValue ? Unauthorized() : Result(await jobs.CancelAsync(CurrentUserId.Value, id, token));
    [HttpPost("{id:guid}/retry"), EnableRateLimiting("metadata")]
    public async Task<IActionResult> Retry(Guid id, CancellationToken token) => !CurrentUserId.HasValue ? Unauthorized() : Result(await jobs.RetryAsync(CurrentUserId.Value, id, token));
    [HttpPost("{id:guid}/dismiss")]
    public async Task<IActionResult> Dismiss(Guid id, CancellationToken token) => !CurrentUserId.HasValue ? Unauthorized() : Result(await jobs.DismissAsync(CurrentUserId.Value, id, token));
    [HttpPost("{id:guid}/accept-listed-critics"), EnableRateLimiting("metadata")]
    public async Task<IActionResult> AcceptListedCritics(Guid id, CancellationToken token) => !CurrentUserId.HasValue ? Unauthorized() : Result(await jobs.AcceptListedCriticsAsync(CurrentUserId.Value, id, token));
    [HttpPost("{id:guid}/items/{gameId:int}/review")]
    public async Task<IActionResult> Review(Guid id, int gameId, GameMetadataApplyRequest request, CancellationToken token) => !CurrentUserId.HasValue ? Unauthorized() : Result(await jobs.ApplyReviewAsync(CurrentUserId.Value, id, gameId, request, token));
}
