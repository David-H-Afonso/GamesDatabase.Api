using System.Security.Claims;
using System.Text.Json;
using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Application.Services;
using GamesDatabase.Api.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GamesDatabase.Api.Tests;

// Real controller error projection only. These downstream failures are fixtures;
// actual middleware/owner guards are tested by ExportZipHostIntegrationTests.
public sealed class ExportErrorContractsTests
{
    private const string InternalDetail = "SYNTHETIC_INTERNAL_DETAIL at C:/fixture-only/private-path";
    private static ExportController Controller(bool admin)
    {
        var http = new DefaultHttpContext();
        http.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "7001"), new Claim(ClaimTypes.Role, admin ? "Admin" : "Standard")
        }, "Synthetic test identity"));
        return new ExportController(new FailingZip(), new FailingNetwork(), NullLogger<ExportController>.Instance,
            new ConfigurationBuilder().AddInMemoryCollection().Build()) { ControllerContext = new ControllerContext { HttpContext = http } };
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ZIP_unexpected_failures_do_not_return_exception_messages_or_internal_paths(bool admin)
    {
        var result = Assert.IsType<ObjectResult>(await Controller(admin).GetZip(true));
        Assert.Equal(500, result.StatusCode);
        var body = JsonSerializer.Serialize(result.Value);
        Assert.DoesNotContain("SYNTHETIC_INTERNAL_DETAIL", body);
        Assert.DoesNotContain("private-path", body);
        Assert.Contains("zipExportFailed", body);
    }

    [Fact]
    public async Task Network_unexpected_failure_does_not_return_stack_type_inner_exception_or_internal_paths()
    {
        var result = Assert.IsType<ObjectResult>(await Controller(true).SyncToNetwork(new(false)));
        Assert.Equal(500, result.StatusCode);
        var body = JsonSerializer.Serialize(result.Value);
        Assert.DoesNotContain("SYNTHETIC_INTERNAL_DETAIL", body);
        Assert.DoesNotContain("private-path", body);
        Assert.DoesNotContain("stackTrace", body);
        Assert.DoesNotContain("innerError", body);
        Assert.Contains("networkSyncFailed", body);
    }

    [Fact]
    public async Task Standard_user_network_failure_is_safe_without_a_blanket_admin_restriction()
    {
        var result = Assert.IsType<ObjectResult>(await Controller(false).SyncToNetwork(new(false)));
        Assert.Equal(500, result.StatusCode);
        Assert.DoesNotContain("SYNTHETIC_INTERNAL_DETAIL", JsonSerializer.Serialize(result.Value));
    }

    private sealed class FailingZip : IZipExportService
    {
        public Task<ZipExportResult> BuildZipAsync(string? authorizationHeader, bool fullExport = false) => throw new InvalidOperationException(InternalDetail);
    }
    private sealed class FailingNetwork : INetworkSyncService
    {
        public Task<NetworkSyncResult> SyncToNetworkAsync(int userId, string? authorizationHeader, bool fullSync = false) => throw new InvalidOperationException(InternalDetail, new IOException("SYNTHETIC_INTERNAL_DETAIL"));
        public Task<FolderAnalysisResult> AnalyzeFoldersAsync(int userId) => throw new NotSupportedException();
        public Task<DatabaseDuplicatesResult> AnalyzeDatabaseDuplicatesAsync(int userId) => throw new NotSupportedException();
        public Task<int> DismissDuplicateGamesAsync(int userId, IReadOnlyCollection<int> gameIds) => throw new NotSupportedException();
    }
}
