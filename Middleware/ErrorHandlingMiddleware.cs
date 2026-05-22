using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Text.Json;

namespace GamesDatabase.Api.Middleware;

public class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;

    public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unhandled exception occurred");
            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        var response = new ErrorResponse();

        switch (exception)
        {
            case DbUpdateException dbEx when dbEx.InnerException is SqliteException sqliteEx:
                response = HandleSqliteException(sqliteEx);
                break;

            case DbUpdateException dbEx:
                response = new ErrorResponse
                {
                    StatusCode = (int)HttpStatusCode.BadRequest,
                    Message = "Error saving data",
                    Details = "Please verify the data is valid and no duplicates exist"
                };
                break;

            case ArgumentException argEx:
                response = new ErrorResponse
                {
                    StatusCode = (int)HttpStatusCode.BadRequest,
                    Message = "Invalid data",
                    Details = argEx.Message
                };
                break;

            case UnauthorizedAccessException:
                response = new ErrorResponse
                {
                    StatusCode = (int)HttpStatusCode.Unauthorized,
                    Message = "Unauthorized",
                    Details = "You do not have permission to perform this action"
                };
                break;

            case KeyNotFoundException:
                response = new ErrorResponse
                {
                    StatusCode = (int)HttpStatusCode.NotFound,
                    Message = "Resource not found",
                    Details = "The requested item does not exist"
                };
                break;

            default:
                response = new ErrorResponse
                {
                    StatusCode = (int)HttpStatusCode.InternalServerError,
                    Message = "Internal server error",
                    Details = "An unexpected error occurred. Please try again"
                };
                break;
        }

        context.Response.StatusCode = response.StatusCode;

        var jsonResponse = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await context.Response.WriteAsync(jsonResponse);
    }

    private static ErrorResponse HandleSqliteException(SqliteException sqliteEx)
    {
        return sqliteEx.SqliteErrorCode switch
        {
            19 when sqliteEx.Message.Contains("UNIQUE constraint failed") => HandleUniqueConstraintError(sqliteEx.Message),
            19 when sqliteEx.Message.Contains("FOREIGN KEY constraint failed") => new ErrorResponse
            {
                StatusCode = (int)HttpStatusCode.BadRequest,
                Message = "Foreign key reference error",
                Details = $"Invalid foreign key value. Verify that StatusId, PlatformId, PlayWithId, and PlayedStatusId have valid values. Original error: {sqliteEx.Message}"
            },
            19 when sqliteEx.Message.Contains("NOT NULL constraint failed") => new ErrorResponse
            {
                StatusCode = (int)HttpStatusCode.BadRequest,
                Message = "Required field missing",
                Details = "Required data is missing to complete the operation"
            },
            _ => new ErrorResponse
            {
                StatusCode = (int)HttpStatusCode.BadRequest,
                Message = "Database error",
                Details = "Could not complete the operation. Please verify the data and try again"
            }
        };
    }

    private static ErrorResponse HandleUniqueConstraintError(string sqliteMessage)
    {
        var field = ExtractFieldFromUniqueConstraintMessage(sqliteMessage);
        var friendlyFieldName = GetFriendlyFieldName(field);

        return new ErrorResponse
        {
            StatusCode = (int)HttpStatusCode.Conflict,
            Message = $"An item with this {friendlyFieldName} already exists",
            Details = $"The {friendlyFieldName} must be unique. Please use a different {friendlyFieldName}."
        };
    }

    private static string ExtractFieldFromUniqueConstraintMessage(string message)
    {
        // Extract field name from "UNIQUE constraint failed: table.field"
        var parts = message.Split(':');
        if (parts.Length > 1)
        {
            var tableDotField = parts[1].Trim();
            var fieldParts = tableDotField.Split('.');
            if (fieldParts.Length > 1)
            {
                return fieldParts[1];
            }
        }
        return "field";
    }

    private static string GetFriendlyFieldName(string field)
    {
        return field.ToLower() switch
        {
            "name" => "name",
            "email" => "email",
            "username" => "username",
            "code" => "code",
            _ => "value"
        };
    }
}

public class ErrorResponse
{
    public int StatusCode { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}