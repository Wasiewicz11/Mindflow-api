using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Mindflow.Api.Services.Ai;

public static partial class DayPlanPromptBuilder
{
    public const int MaxTimelineItems = 12;
    public const int MaxFocusItems = 3;
    public const int MaxRiskItems = 4;
    public const int MaxSummaryLength = 400;
    public const int MaxTitleLength = 120;
    public const int MaxNoteLength = 200;
    public const int MaxListItemLength = 160;
    public const int MaxTipLength = 300;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly string[] TimeFormats = ["HH:mm", "H:mm"];

    public static string SystemInstruction() =>
        $$"""
        Jesteś asystentem planowania dnia w aplikacji do zarządzania zadaniami.
        Dostajesz dzisiejszą datę, godzinę startu dnia ("dayStart"), stałe bloki z kalendarza ("blocks")
        oraz otwarte zadania użytkownika (każde ma numer "ref", priorytet P1-P4, gdzie P1 jest najważniejszy,
        "daysUntilDue": 0 = termin dziś, liczba ujemna = zadanie po terminie, opcjonalnie "dueTime" i "estimatedHours").
        Przygotuj szczegółowy, realistyczny plan dzisiejszego dnia po polsku.

        Zwróć WYŁĄCZNIE poprawny JSON w formacie:
        {
          "summary": "1-2 zdania: jaki to dzień i jaki jest główny cel",
          "timeline": [
            { "start": "HH:mm", "end": "HH:mm", "title": "tytuł zadania albo bloku", "note": "krótko: dlaczego teraz albo jak to zrobić" }
          ],
          "focus": ["1-3 najważniejsze rzeczy, które trzeba dziś zrobić"],
          "risks": ["krótko: zaległości, konflikty w kalendarzu, przeciążenie"],
          "tip": "jedna praktyczna wskazówka na dziś"
        }

        Zasady:
        - Bloki z kalendarza są stałe: umieść je dokładnie w ich godzinach i nie planuj na nie niczego innego.
        - Zadania z "dueTime" umieść o tej godzinie.
        - Czas trwania zadania bierz z "estimatedHours"; gdy go brak, przyjmij 30-60 minut zależnie od zadania.
        - Wolne okna wypełnij zadaniami na dziś i zaległymi, najpierw P1 i zaległe, potem pozostałe według priorytetu.
        - Dodaj jedną krótką przerwę (15-30 minut).
        - Zacznij nie wcześniej niż "dayStart" i skończ najpóźniej o 22:00. Godziny w formacie 24h "HH:mm", tylko dzisiejszy dzień.
        - Maksymalnie {{MaxTimelineItems}} punktów w "timeline", posortowanych według godziny.
        - Używaj dokładnych tytułów zadań i bloków z wejścia. Nie wymyślaj nowych zadań poza przerwą.
        - "focus" ma 1-3 pozycje, "risks" może być pustą listą.
        - Żadnego tekstu poza JSON-em.
        """;

    public static string BuildUserPayload(DayPlanContext context) =>
        JsonSerializer.Serialize(new
        {
            date = context.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            dayStart = Format(context.DayStart),
            blocks = context.Blocks.Select(b => new { start = Format(b.Start), end = Format(b.End), title = b.Title }),
            tasks = context.Tasks.Select(t => new
            {
                @ref = t.Ref,
                title = t.Title,
                priority = t.Priority,
                status = t.Status,
                daysUntilDue = t.DaysUntilDue,
                dueTime = t.DueTime is { } time ? Format(time) : null,
                estimatedHours = t.EstimatedHours,
                timesPostponed = t.TimesPostponed
            })
        }, JsonOptions);

    public static DayPlan? Parse(string? rawText, DayPlanContext context)
    {
        if (string.IsNullOrWhiteSpace(rawText)) return null;

        LlmPlan? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<LlmPlan>(ExtractJson(rawText), JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        if (parsed is null || string.IsNullOrWhiteSpace(parsed.Summary)) return null;

        var titles = context.Tasks.ToDictionary(t => t.Ref, t => t.Title);
        var timeline = new List<DayPlanItem>();
        foreach (var item in parsed.Timeline ?? [])
        {
            if (string.IsNullOrWhiteSpace(item.Title)
                || !TimeOnly.TryParseExact(item.Start?.Trim(), TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
                || !TimeOnly.TryParseExact(item.End?.Trim(), TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var end)
                || end <= start)
                continue;

            var note = string.IsNullOrWhiteSpace(item.Note) ? null : Clean(item.Note, titles, MaxNoteLength);
            timeline.Add(new DayPlanItem(start, end, Clean(item.Title, titles, MaxTitleLength), note));
        }

        return new DayPlan(
            Clean(parsed.Summary, titles, MaxSummaryLength),
            timeline.OrderBy(i => i.Start).Take(MaxTimelineItems).ToList(),
            CleanList(parsed.Focus, titles, MaxFocusItems),
            CleanList(parsed.Risks, titles, MaxRiskItems),
            string.IsNullOrWhiteSpace(parsed.Tip) ? null : Clean(parsed.Tip, titles, MaxTipLength),
            FromAi: true);
    }

    public static string Truncate(string value, int max)
    {
        value = value.Trim();
        if (value.Length <= max) return value;
        var cut = char.IsHighSurrogate(value[max - 2]) ? max - 2 : max - 1;
        return $"{value[..cut]}…";
    }

    private static string Format(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);

    private static List<string> CleanList(IEnumerable<string?>? values, IReadOnlyDictionary<int, string> titles, int max) =>
        (values ?? [])
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => Clean(v!, titles, MaxListItemLength))
            .Take(max)
            .ToList();

    private static string Clean(string value, IReadOnlyDictionary<int, string> titles, int max) =>
        Truncate(TaskRef().Replace(value, match =>
            int.TryParse(match.Groups[1].Value, out var reference) && titles.TryGetValue(reference, out var title)
                ? title
                : match.Value), max);

    private static string ExtractJson(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : text;
    }

    [GeneratedRegex(@"#(\d+)\b")]
    private static partial Regex TaskRef();

    private record LlmPlan(string? Summary, List<LlmItem>? Timeline, List<string?>? Focus, List<string?>? Risks, string? Tip);
    private record LlmItem(string? Start, string? End, string? Title, string? Note);
}
