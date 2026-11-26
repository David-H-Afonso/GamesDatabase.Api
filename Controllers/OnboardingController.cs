using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GamesDatabase.Api.Controllers;

[ApiController, Authorize, Route("api/onboarding")]
public sealed class OnboardingController(IOnboardingService guide) : BaseApiController
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        if (!CurrentUserId.HasValue) return Unauthorized();
        Response.Headers.CacheControl = "no-store";
        return Ok(await guide.GetAsync(CurrentUserId.Value, cancellationToken));
    }

    [HttpPost("start")]
    public async Task<IActionResult> Start(OnboardingStartRequest request, CancellationToken cancellationToken)
    {
        if (!CurrentUserId.HasValue) return Unauthorized();
        var result = await guide.StartAsync(CurrentUserId.Value, request, cancellationToken);
        return result.Success ? Ok(result.Data) : result.Conflict ? Conflict(new { code = "onboardingConflict" }) : result.NotFound ? NotFound() : BadRequest(new { code = "invalidOnboarding" });
    }

    [HttpPut]
    public async Task<IActionResult> Save(OnboardingProgressRequest request, CancellationToken cancellationToken)
    {
        if (!CurrentUserId.HasValue) return Unauthorized();
        var result = await guide.SaveAsync(CurrentUserId.Value, request, cancellationToken);
        return result.Success ? Ok(result.Data) : result.Conflict ? Conflict(new { code = "onboardingConflict" }) : result.NotFound ? NotFound() : BadRequest(new { code = "invalidOnboarding" });
    }

    [HttpPost("samples")]
    public async Task<IActionResult> CreateSamples(CreateTutorialSamplesRequest request, CancellationToken cancellationToken)
    {
        if (!CurrentUserId.HasValue) return Unauthorized();
        var result = await guide.CreateSamplesAsync(CurrentUserId.Value, request, cancellationToken);
        return result.Success ? Ok(result.Data) : result.Conflict ? Conflict(new { code = "onboardingConflict" }) : result.NotFound ? NotFound() : BadRequest(new { code = "invalidOnboarding" });
    }

    [HttpPost("samples/keep")]
    public Task<IActionResult> Keep(ChangeTutorialSamplesRequest request, CancellationToken cancellationToken) => Change(request, false, cancellationToken);

    [HttpDelete("samples")]
    public Task<IActionResult> Remove(ChangeTutorialSamplesRequest request, CancellationToken cancellationToken) => Change(request, true, cancellationToken);

    private async Task<IActionResult> Change(ChangeTutorialSamplesRequest request, bool remove, CancellationToken cancellationToken)
    {
        if (!CurrentUserId.HasValue) return Unauthorized();
        var result = await guide.ChangeSamplesAsync(CurrentUserId.Value, request, remove, cancellationToken);
        return result.Success ? Ok(result.Data) : result.Conflict ? Conflict(new { code = "onboardingConflict" }) : BadRequest(new { code = "invalidOnboarding" });
    }
}
