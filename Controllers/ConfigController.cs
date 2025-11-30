using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using GamesDatabase.Api.Configuration;

namespace GamesDatabase.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ConfigController : BaseApiController
{
    private readonly IOptionsMonitor<NetworkSyncOptions> _syncOptions;
    private readonly IConfiguration _configuration;

    public ConfigController(
        IOptionsMonitor<NetworkSyncOptions> syncOptions,
        IConfiguration configuration)
    {
        _syncOptions = syncOptions;
        _configuration = configuration;
    }

    /// <summary>
    /// Get network sync configuration (path only, no credentials)
    /// Public endpoint - no authentication required
    /// </summary>
    [AllowAnonymous]
    [HttpGet("network-sync")]
    public IActionResult GetNetworkSyncConfig()
    {
        var options = _syncOptions.CurrentValue;
        return Ok(new
        {
            enabled = options.Enabled,
            networkPath = options.NetworkPath
            // Don't expose credentials
        });
    }
}
