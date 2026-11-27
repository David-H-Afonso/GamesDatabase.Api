using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GamesDatabase.Api.Infrastructure.Persistence;
using GamesDatabase.Api.Common;
using Microsoft.EntityFrameworkCore;

namespace GamesDatabase.Api.Controllers;

[ApiController]
[Route("api/gameplatforms")]
[AllowAnonymous]
public sealed class GamePlatformLogoController(GamesDbContext context) : ControllerBase
{
    [HttpGet("{id:int}/logo")]
    public async Task<IActionResult> GetLogo(int id, CancellationToken cancellationToken)
    {
        var logo = await context.GamePlatforms.AsNoTracking()
            .Where(platform => platform.Id == id && platform.Logo != null && platform.Logo.Length <= PlatformLogoContent.MaximumLength)
            .Select(platform => platform.Logo).FirstOrDefaultAsync(cancellationToken);
        logo = PlatformLogoContent.Normalize(logo);
        if (logo is null) return NotFound();
        // Revalidate old/imported records at the serving boundary. Never trust a
        // stored MIME declaration or redirect schemes saved by older clients.
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Content-Security-Policy"] = PlatformLogoContent.ContentSecurityPolicy;
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers.CacheControl = "no-store";
        if (PlatformLogoContent.IsSafeReference(logo)) return Redirect(logo);
        if (!PlatformLogoContent.TryReadDataUrl(logo, out var image)) return NotFound();
        return File(image!.Bytes, image.MediaType);
    }
}
