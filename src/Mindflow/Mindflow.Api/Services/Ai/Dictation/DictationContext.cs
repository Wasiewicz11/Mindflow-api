namespace Mindflow.Api.Services.Ai.Dictation;

public record DictationProject(Guid Id, string Name);

public record DictationContext(DateOnly Today, TimeOnly Now, string TimeZone, IReadOnlyList<DictationProject> Projects)
{
    private static readonly string[] Weekdays = ["niedziela", "poniedziałek", "wtorek", "środa", "czwartek", "piątek", "sobota"];

    public static string WeekdayOf(DateOnly date) => Weekdays[(int)date.DayOfWeek];

    public IReadOnlyList<string> ProjectNames => Projects.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}

public record DictationInput(string? Text, byte[]? Audio, string? AudioMimeType)
{
    public bool IsAudio => Audio is { Length: > 0 };
}
