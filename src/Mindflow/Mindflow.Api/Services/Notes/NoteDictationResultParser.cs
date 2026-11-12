using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Services.Ai.Dictation;

namespace Mindflow.Api.Services.Notes;

public record NoteDictationResult(
    string Title,
    DateOnly? EventDate,
    List<string> Tags,
    string Markdown,
    string Transcript,
    IReadOnlyList<DictatedTaskResponse> Tasks);

/// <summary>Turns the model's JSON into a note; returns null when the answer is not a JSON object.</summary>
public static partial class NoteDictationResultParser
{
    public const int MaxTitle = 200;
    public const int MaxContent = 100_000;
    private const int MaxTags = 5;

    public static NoteDictationResult? Parse(string? raw, DictationContext context, int maxTasks)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(DictationResultParser.StripFences(raw));
            root = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
        if (root.ValueKind != JsonValueKind.Object) return null;

        var dictated = DictationResultParser.Parse(raw, context, maxTasks);
        var title = Clip(Collapse(String(root, "title")), MaxTitle) ?? $"Notatka z {context.Today:dd.MM.yyyy}";
        var markdown = Clip(Html().Replace(String(root, "markdown") ?? "", "").Trim(), MaxContent)!;

        return new NoteDictationResult(
            title,
            ParseEventDate(String(root, "eventDate"), context.Today),
            ParseTags(root),
            markdown,
            dictated.Transcript,
            dictated.Tasks);
    }

    private static DateOnly? ParseEventDate(string? value, DateOnly today)
    {
        if (!DateOnly.TryParseExact(value?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return null;
        return date < today.AddYears(-5) || date > today.AddYears(5) ? null : date;
    }

    private static List<string> ParseTags(JsonElement root)
    {
        if (!root.TryGetProperty("tags", out var tags) || tags.ValueKind != JsonValueKind.Array) return [];
        var values = tags.EnumerateArray()
            .Where(t => t.ValueKind == JsonValueKind.String)
            .Select(t => t.GetString()!.Replace("\0", "").Trim().TrimStart('#'));
        return NoteText.NormalizeTags(values).Take(MaxTags).ToList();
    }

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()?.Replace("\0", "") : null;

    private static string? Collapse(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string? Clip(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max];

    [GeneratedRegex(@"</?[A-Za-z][A-Za-z0-9]*(\s[^>]*)?/?>")]
    private static partial Regex Html();
}
