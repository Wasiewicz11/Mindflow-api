using System.Text.RegularExpressions;

namespace Mindflow.Api.Services.Ai.Providers;

public static partial class AiModelName
{
    public static bool IsValid(string? model) => model is not null && Pattern().IsMatch(model);

    [GeneratedRegex("^[a-zA-Z0-9][a-zA-Z0-9._:-]{1,99}$")]
    private static partial Regex Pattern();
}
