using System.Net;

namespace Mindflow.Api.Exceptions;

/// <summary>Rendered by the exception middleware as <c>{"error": "&lt;code&gt;"}</c>, the OAuth 2.0 error format.</summary>
public class OAuthException(string error) : ApiException(error, (int)HttpStatusCode.BadRequest)
{
    public const string InvalidRequest = "invalid_request";
    public const string InvalidClient = "invalid_client";
    public const string InvalidGrant = "invalid_grant";
    public const string UnsupportedGrantType = "unsupported_grant_type";
}
