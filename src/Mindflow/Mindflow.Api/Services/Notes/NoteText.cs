using System.Text.RegularExpressions;

namespace Mindflow.Api.Services.Notes;

/// <summary>Plain-text preview of a markdown note and tag normalization.</summary>
public static partial class NoteText
{
    public const int PreviewLength = 160;
    public const int MaxTags = 20;
    public const int MaxTagLength = 40;

    public static string Preview(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return "";

        var parts = new List<string>();
        var inCode = false;
        foreach (var raw in markdown.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                inCode = !inCode;
                continue;
            }
            if (inCode || line.Length == 0 || HorizontalRule().IsMatch(line)) continue;

            line = BlockPrefix().Replace(line, "");
            line = Link().Replace(line, "$1");
            line = Inline().Replace(line, "").Trim();
            if (line.Length > 0) parts.Add(line);
        }

        var text = Spaces().Replace(string.Join(' ', parts), " ").Trim();
        if (text.Length <= PreviewLength) return text;
        var cut = PreviewLength - 1;
        if (char.IsHighSurrogate(text[cut - 1])) cut--;
        return text[..cut].TrimEnd() + "…";
    }

    public static List<string> NormalizeTags(IEnumerable<string>? tags) =>
        (tags ?? [])
            .Select(t => (t ?? "").Trim().ToLowerInvariant())
            .Where(t => t.Length > 0)
            .Select(CutTag)
            .Distinct(StringComparer.Ordinal)
            .Take(MaxTags)
            .ToList();

    private static string CutTag(string t)
    {
        if (t.Length <= MaxTagLength) return t;
        var cut = char.IsHighSurrogate(t[MaxTagLength - 1]) ? MaxTagLength - 1 : MaxTagLength;
        return t[..cut];
    }

    [GeneratedRegex(@"^(-{3,}|\*{3,}|_{3,})$")]
    private static partial Regex HorizontalRule();

    [GeneratedRegex(@"^(#{1,6}\s+|>\s?|[-*+]\s+(\[[ xX]\]\s+)?|\d+\.\s+)+")]
    private static partial Regex BlockPrefix();

    [GeneratedRegex(@"\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"\*\*|__|~~|[*`]")]
    private static partial Regex Inline();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
