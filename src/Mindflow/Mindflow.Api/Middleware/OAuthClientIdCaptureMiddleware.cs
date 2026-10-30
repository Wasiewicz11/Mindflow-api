using System.Text.Json;
using Mindflow.Api.Services.Integrations;

namespace Mindflow.Api.Middleware;

/// <summary>Exposes client_id of a token request to the rate limiter, which cannot read the body itself.</summary>
public class OAuthClientIdCaptureMiddleware(RequestDelegate next)
{
    public const string ItemKey = "oauth_client_id";
    private const long MaxJsonBodyBytes = 16 * 1024;

    public async Task InvokeAsync(HttpContext context)
    {
        if (HttpMethods.IsPost(context.Request.Method)
            && context.Request.Path.Equals(IntegrationRoutes.OAuthTokenPath, StringComparison.OrdinalIgnoreCase))
        {
            context.Items[ItemKey] = await ReadClientIdAsync(context.Request, context.RequestAborted);
        }

        await next(context);
    }

    private static async Task<string?> ReadClientIdAsync(HttpRequest request, CancellationToken ct)
    {
        try
        {
            if (request.HasFormContentType)
            {
                var form = await request.ReadFormAsync(ct);
                return form["client_id"].FirstOrDefault();
            }

            if (request.HasJsonContentType() && request.ContentLength is null or <= MaxJsonBodyBytes)
            {
                request.EnableBuffering();
                using var document = await JsonDocument.ParseAsync(request.Body, cancellationToken: ct);
                request.Body.Position = 0;

                return document.RootElement.ValueKind == JsonValueKind.Object
                       && document.RootElement.TryGetProperty("client_id", out var clientId)
                       && clientId.ValueKind == JsonValueKind.String
                    ? clientId.GetString()
                    : null;
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            if (request.Body.CanSeek) request.Body.Position = 0;
        }

        return null;
    }
}
