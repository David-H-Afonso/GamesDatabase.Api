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
                    Message = "Error al guardar los datos",
                    Details = "Verifique que los datos sean válidos y no existan duplicados"
                };
                break;

            case ArgumentException argEx:
                response = new ErrorResponse
                {
                    StatusCode = (int)HttpStatusCode.BadRequest,
                    Message = "Datos inválidos",
                    Details = argEx.Message
                };
                break;

            case UnauthorizedAccessException:
                response = new ErrorResponse
                {
                    StatusCode = (int)HttpStatusCode.Unauthorized,
                    Message = "No autorizado",
                    Details = "No tiene permisos para realizar esta acción"
                };
                break;

            case KeyNotFoundException:
                response = new ErrorResponse
                {
                    StatusCode = (int)HttpStatusCode.NotFound,
                    Message = "Recurso no encontrado",
                    Details = "El elemento solicitado no existe"
                };
                break;

            default:
                response = new ErrorResponse
                {
                    StatusCode = (int)HttpStatusCode.InternalServerError,
                    Message = "Error interno del servidor",
                    Details = "Ha ocurrido un error inesperado. Por favor, intente nuevamente"
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
                Message = "Error de referencia de clave foránea",
                Details = $"Valor de clave foránea inválido. Verifique que StatusId, PlatformId, PlayWithId y PlayedStatusId tengan valores válidos. Error original: {sqliteEx.Message}"
            },
            19 when sqliteEx.Message.Contains("NOT NULL constraint failed") => new ErrorResponse
            {
                StatusCode = (int)HttpStatusCode.BadRequest,
                Message = "Campo requerido",
                Details = "Faltan datos obligatorios para completar la operación"
            },
            _ => new ErrorResponse
            {
                StatusCode = (int)HttpStatusCode.BadRequest,
                Message = "Error en la base de datos",
                Details = "No se pudo completar la operación. Verifique los datos e intente nuevamente"
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
            Message = $"Ya existe un elemento con este {friendlyFieldName}",
            Details = $"El {friendlyFieldName} debe ser único. Por favor, use un {friendlyFieldName} diferente."
        };
    }

    private static string ExtractFieldFromUniqueConstraintMessage(string message)
    {
        // Extraer el campo del mensaje "UNIQUE constraint failed: tabla.campo"
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
        return "campo";
    }

    private static string GetFriendlyFieldName(string field)
    {
        return field.ToLower() switch
        {
            "name" => "nombre",
            "email" => "email",
            "username" => "nombre de usuario",
            "code" => "código",
            _ => "valor"
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