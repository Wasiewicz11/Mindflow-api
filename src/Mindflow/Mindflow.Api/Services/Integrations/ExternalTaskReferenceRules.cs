using System.Text.RegularExpressions;

namespace Mindflow.Api.Services.Integrations;

public static partial class ExternalTaskReferenceRules
{
    public const int MaxExternalIdLength = 200;
    public const int MaxUrlLength = 2048;

    [GeneratedRegex(@"^[a-z0-9-]{1,40}\z")]
    private static partial Regex SourcePattern();

    public static Dictionary<string, string[]> Validate(
        string source,
        string externalId,
        string? externalUrl,
        bool allowLoopbackHttp)
    {
        var errors = new Dictionary<string, string[]>();

        if (!SourcePattern().IsMatch(source))
        {
            errors["source"] = ["Source must have 1-40 characters: lowercase letters, digits or hyphens."];
        }

        if (string.IsNullOrWhiteSpace(externalId)
            || externalId.Length > MaxExternalIdLength
            || externalId.Any(char.IsControl))
        {
            errors["externalId"] = [$"External id must have 1-{MaxExternalIdLength} characters without control characters."];
        }

        if (externalUrl is not null && !IsAllowedUrl(externalUrl, allowLoopbackHttp))
        {
            errors["externalUrl"] = [allowLoopbackHttp
                ? "External URL must be an absolute https:// or http://localhost address."
                : "External URL must be an absolute https:// address."];
        }

        return errors;
    }

    private static bool IsAllowedUrl(string url, bool allowLoopbackHttp)
    {
        if (url.Length > MaxUrlLength || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme == Uri.UriSchemeHttps)
        {
            return true;
        }

        return allowLoopbackHttp && uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback;
    }
}
