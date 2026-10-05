using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using OrderManagement.Exceptions;

namespace OrderManagement.Middleware;

public sealed class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            await HandleAsync(context, ex);
        }
    }

    private async Task HandleAsync(HttpContext context, Exception ex)
    {
        var status = ex switch
        {
            NotFoundException => StatusCodes.Status404NotFound,
            ConflictException => StatusCodes.Status409Conflict,
            UnprocessableEntityException => StatusCodes.Status422UnprocessableEntity,
            DbUpdateConcurrencyException => StatusCodes.Status409Conflict,
            DbUpdateException => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError
        };

        if (status >= 500)
            logger.LogError(ex, "Unhandled exception for {Path}", context.Request.Path);
        else
            logger.LogWarning(ex, "API error for {Path}", context.Request.Path);

        var correlationId = context.Items["X-Correlation-ID"]?.ToString() ?? context.TraceIdentifier;
        var response = new
        {
            type = $"https://api.example.com/errors/{status}",
            title = status switch
            {
                404 => "Resource not found",
                409 => "Conflict",
                422 => "Business validation failed",
                _ => "Internal server error"
            },
            status,
            detail = status == 500 ? "An unexpected error occurred." : ex.Message,
            traceId = correlationId
        };

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(response));
    }
}
