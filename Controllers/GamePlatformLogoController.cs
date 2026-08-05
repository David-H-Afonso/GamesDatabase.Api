using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GamesDatabase.Api.Infrastructure.Persistence;
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
        var logo = await context.GamePlatforms.AsNoTracking().Where(platform => platform.Id == id).Select(platform => platform.Logo).FirstOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(logo)) return NotFound();
        if (!logo.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)) return Redirect(logo);

        var comma = logo.IndexOf(',');
        if (comma < 0) return NotFound();
        try
        {
            var bytes = Convert.FromBase64String(logo[(comma + 1)..]);
            var mediaType = logo[5..comma].Split(';')[0];
            return File(bytes, mediaType);
        }
        catch (FormatException)
        {
            return NotFound();
        }
    }
}
