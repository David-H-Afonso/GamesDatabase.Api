using System.Net;
using System.Reflection;
using GamesDatabase.Api.Application.Interfaces;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GamesDatabase.Api.Tests;

public sealed class HealthErrorContractsTests
{
    [Fact]
    public async Task Anonymous_health_failure_returns_status_without_exposing_internal_exception_details()
    {
        using var root = new GamesDatabaseApiFactory();
        using var factory = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddScoped(_ => DispatchProxy.Create<IUserService, FailedHealthFixture>())));
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/users/health");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("unhealthy", body);
        Assert.DoesNotContain("SYNTHETIC_INTERNAL_HEALTH_DETAIL", body);
        Assert.DoesNotContain("private-fixture-path", body);
    }

    public class FailedHealthFixture : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException("SYNTHETIC_INTERNAL_HEALTH_DETAIL at C:/private-fixture-path");
    }
}
