using System.Net;
using System.Text.Json;
using Mindflow.Api.Exceptions;

namespace Mindflow.Api.Middleware;

public class ApiExceptionHandlingMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (ApiException ex)
        {
            await WriteErrorResponseAsync(context, ex.StatusCode, ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            await WriteErrorResponseAsync(context, (int)HttpStatusCode.Unauthorized, ex.Message);
        }
    }

    private static Task WriteErrorResponseAsync(HttpContext context, int statusCode, string message)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";

        var payload = JsonSerializer.Serialize(new
        {
            error = message
        });

        return context.Response.WriteAsync(payload);
    }
}
