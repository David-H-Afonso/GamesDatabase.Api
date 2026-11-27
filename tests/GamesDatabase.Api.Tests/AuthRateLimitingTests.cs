using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace GamesDatabase.Api.Tests;

public sealed class AuthRateLimitingTests
{
    private static async Task<int> Attempt(GamesDatabaseApiFactory factory, string? address, int forgedAddress = 1, string path = "/api/users/login")
    {
        var payload = Encoding.UTF8.GetBytes(path.EndsWith("refresh", StringComparison.Ordinal) ? "{\"refreshToken\":\"invalid-synthetic-refresh\"}" : "{\"username\":\"nonexistent-rate-limit-fixture\",\"password\":\"invalid-synthetic-password\"}");
        var response = await factory.Server.SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = address is null ? null : IPAddress.Parse(address);
            context.Request.Method = HttpMethods.Post; context.Request.Path = path; context.Request.ContentType = "application/json";
            context.Request.Body = new MemoryStream(payload); context.Request.ContentLength = payload.Length;
            context.Request.Headers["X-Forwarded-For"] = $"198.51.100.{forgedAddress}";
            context.Request.Headers["X-Real-IP"] = $"203.0.113.{forgedAddress}";
            context.Request.Headers["Forwarded"] = $"for=198.51.100.{forgedAddress}";
        });
        return response.Response.StatusCode;
    }

    [Fact]
    public async Task Exhausting_client_A_does_not_consume_client_Bs_auth_allowance()
    {
        using var factory = new GamesDatabaseApiFactory(); using var client = factory.CreateClient();
        for (var index = 0; index < 10; index++) Assert.Equal(StatusCodes.Status401Unauthorized, await Attempt(factory, "192.0.2.10"));
        Assert.Equal(StatusCodes.Status429TooManyRequests, await Attempt(factory, "192.0.2.10"));
        Assert.Equal(StatusCodes.Status401Unauthorized, await Attempt(factory, "192.0.2.11"));
    }

    [Fact]
    public async Task Arbitrary_forwarding_headers_cannot_rotate_the_connection_partition_or_separate_auth_endpoints()
    {
        using var factory = new GamesDatabaseApiFactory(); using var client = factory.CreateClient();
        for (var index = 0; index < 10; index++) Assert.Equal(StatusCodes.Status401Unauthorized, await Attempt(factory, "192.0.2.20", index + 1));
        Assert.Equal(StatusCodes.Status429TooManyRequests, await Attempt(factory, "192.0.2.20", 99));
        Assert.Equal(StatusCodes.Status429TooManyRequests, await Attempt(factory, "192.0.2.20", 100, "/api/users/refresh"));
        Assert.Equal(StatusCodes.Status401Unauthorized, await Attempt(factory, "192.0.2.21", 100, "/api/users/refresh"));
    }

    [Fact]
    public async Task IPv4_and_IPv4_mapped_IPv6_share_one_partition_while_native_IPv6_is_independent()
    {
        using var factory = new GamesDatabaseApiFactory(); using var client = factory.CreateClient();
        for (var index = 0; index < 10; index++) Assert.Equal(StatusCodes.Status401Unauthorized, await Attempt(factory, "192.0.2.30"));
        Assert.Equal(StatusCodes.Status429TooManyRequests, await Attempt(factory, "::ffff:192.0.2.30"));
        Assert.Equal(StatusCodes.Status401Unauthorized, await Attempt(factory, "2001:db8::30"));
    }

    [Fact]
    public async Task Missing_connection_identity_is_fail_closed_and_never_uses_a_forwarded_header()
    {
        using var factory = new GamesDatabaseApiFactory(); using var client = factory.CreateClient();
        for (var index = 0; index < 10; index++) Assert.Equal(StatusCodes.Status401Unauthorized, await Attempt(factory, null, index + 1));
        Assert.Equal(StatusCodes.Status429TooManyRequests, await Attempt(factory, null, 99));
    }
}
