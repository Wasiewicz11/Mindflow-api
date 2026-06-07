using System.Text.Json;
using Mindflow.Api.Models.Enums;

namespace Mindflow.Api.Services.Ai;

public static class SuggestionPromptBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string SystemInstruction(int maxSuggestions) =>
        $$"""
        Jesteś asystentem produktywności w aplikacji do zarządzania zadaniami.
        Dostajesz listę zadań użytkownika w formie zanonimizowanej (każde ma numer "ref").
        Twoim zadaniem jest przygotować dzienny przegląd: krótkie, konkretne sugestie
        przeorganizowania dnia (po polsku), tak by użytkownik wstał rano i wiedział, co robić.

        Zwróć WYŁĄCZNIE poprawny JSON w formacie:
        {
          "suggestions": [
            {
              "title": "krótki nagłówek karty",
              "body": "1-3 zdania wyjaśnienia, naturalnym językiem, odwołując się do tytułów zadań",
              "actions": [
                { "taskRef": <liczba z wejścia>, "actionType": "ChangeDueDate", "payload": { "date": "YYYY-MM-DD" } },
                { "taskRef": <liczba z wejścia>, "actionType": "ChangePriority", "payload": { "priority": "P1" } }
              ]
            }
          ]
        }

        Zasady:
        - Maksymalnie {{maxSuggestions}} sugestii. Każda sugestia może mieć 1+ akcji.
        - actionType TYLKO jedna z: "ChangeDueDate" (payload.date = data ISO), "ChangePriority" (payload.priority = P1/P2/P3/P4).
        - Używaj wyłącznie taskRef obecnych na wejściu. Nie wymyślaj zadań.
        - Skup się na zadaniach po terminie (ujemne daysUntilDue) i często odkładanych (timesPostponed).
        - Żadnego tekstu poza JSON-em.
        """;

    public static string BuildUserPayload(DaySnapshot snapshot)
        => JsonSerializer.Serialize(snapshot, JsonOptions);

    public static IReadOnlyList<SuggestionDraft> Parse(string? rawText, DaySnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(rawText)) return [];

        var json = ExtractJson(rawText);
        var validRefs = snapshot.Tasks.Select(t => t.Ref).ToHashSet();

        LlmResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<LlmResponse>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return [];
        }

        if (parsed?.Suggestions is null) return [];

        var result = new List<SuggestionDraft>();
        foreach (var s in parsed.Suggestions)
        {
            if (string.IsNullOrWhiteSpace(s.Title) || string.IsNullOrWhiteSpace(s.Body))
                continue;

            var actions = new List<SuggestionActionDraft>();
            foreach (var a in s.Actions ?? [])
            {
                if (!Enum.TryParse<SuggestionActionType>(a.ActionType, ignoreCase: true, out var type))
                    continue;
                if (!validRefs.Contains(a.TaskRef))
                    continue;

                actions.Add(new SuggestionActionDraft(
                    a.TaskRef,
                    type,
                    a.Payload ?? new Dictionary<string, string>()));
            }

            if (actions.Count == 0) continue;

            result.Add(new SuggestionDraft(s.Title.Trim(), s.Body.Trim(), actions));
        }

        return result;
    }

    private static string ExtractJson(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : text;
    }

    private record LlmResponse(List<LlmSuggestion>? Suggestions);
    private record LlmSuggestion(string? Title, string? Body, List<LlmAction>? Actions);
    private record LlmAction(int TaskRef, string? ActionType, Dictionary<string, string>? Payload);
}
