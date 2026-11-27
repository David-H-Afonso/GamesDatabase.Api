using System.Text.Json;
using GamesDatabase.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GamesDatabase.Api.Tests;

public sealed class DatabaseBusyResponseTests
{
    [Theory]
    [InlineData(5, false)] [InlineData(5, true)] [InlineData(6, false)] [InlineData(6, true)]
    public async Task Busy_locks_are_retryable_503_not_invalid_data_400_and_never_expose_internal_details(int code, bool wrapped)
    {
        var databaseError = new SqliteException("PRIVATE_FIXTURE_DB_PATH_AND_SQL", code);
        Exception error = wrapped ? new DbUpdateException("PRIVATE_FIXTURE_ENTITY_DETAIL", databaseError) : databaseError;
        var middleware = new ErrorHandlingMiddleware(_ => throw error, NullLogger<ErrorHandlingMiddleware>.Instance);
        var context = new DefaultHttpContext(); context.Response.Body = new MemoryStream(); await middleware.InvokeAsync(context);
        Assert.Equal(503, context.Response.StatusCode); Assert.Equal("1", context.Response.Headers.RetryAfter.ToString());
        context.Response.Body.Position = 0; var text = await new StreamReader(context.Response.Body).ReadToEndAsync(); using var json = JsonDocument.Parse(text); Assert.Equal("databaseBusy", json.RootElement.GetProperty("code").GetString()); Assert.DoesNotContain("PRIVATE_FIXTURE", text);
    }
}
