using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace Mindflow.Api.Models.Dtos;

public class OAuthTokenRequest
{
    [JsonPropertyName("grant_type"), FromForm(Name = "grant_type")]
    public string? GrantType { get; set; }

    [JsonPropertyName("code"), FromForm(Name = "code")]
    public string? Code { get; set; }

    [JsonPropertyName("redirect_uri"), FromForm(Name = "redirect_uri")]
    public string? RedirectUri { get; set; }

    [JsonPropertyName("client_id"), FromForm(Name = "client_id")]
    public string? ClientId { get; set; }

    [JsonPropertyName("client_secret"), FromForm(Name = "client_secret")]
    public string? ClientSecret { get; set; }

    [JsonPropertyName("code_verifier"), FromForm(Name = "code_verifier")]
    public string? CodeVerifier { get; set; }
}

public record OAuthTokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("token_type")] string TokenType,
    [property: JsonPropertyName("expires_in")] long ExpiresIn,
    [property: JsonPropertyName("scope")] string Scope,
    [property: JsonPropertyName("account")] OAuthAccountResponse Account,
    [property: JsonPropertyName("default_project_id")] Guid? DefaultProjectId);

public record OAuthAccountResponse(string Email, string FirstName, string LastName);
