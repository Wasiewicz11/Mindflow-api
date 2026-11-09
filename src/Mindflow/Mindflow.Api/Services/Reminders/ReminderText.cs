using System.Globalization;

namespace Mindflow.Api.Services.Reminders;

public static class ReminderText
{
    public static (string Title, string Body, string LocalDue) Build(
        string taskTitle, DueReminder due, TimeZoneInfo timeZone, DateTimeOffset now)
    {
        var localDue = FormatLocalDue(due.DueAt, now, timeZone);
        var lead = due.IsSnoozed
            ? "Odłożone przypomnienie"
            : due.MinutesBefore == 0 ? "Teraz" : $"Za {FormatLead(due.MinutesBefore)}";
        return ($"⏰ {Truncate(taskTitle, 80)}", $"{lead} · termin {localDue}", localDue);
    }

    private static string FormatLocalDue(DateTimeOffset dueAt, DateTimeOffset now, TimeZoneInfo timeZone)
    {
        var localDue = TimeZoneInfo.ConvertTime(dueAt, timeZone);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, timeZone).DateTime);
        var dueDay = DateOnly.FromDateTime(localDue.DateTime);
        var time = localDue.ToString("HH:mm", CultureInfo.InvariantCulture);
        if (dueDay == today) return $"dziś {time}";
        if (dueDay == today.AddDays(1)) return $"jutro {time}";
        return $"{localDue.ToString("dd.MM", CultureInfo.InvariantCulture)} {time}";
    }

    private static string FormatLead(int minutes)
    {
        if (minutes < 60) return $"{minutes} min";
        if (minutes % 1440 == 0) return minutes == 1440 ? "1 dzień" : $"{minutes / 1440} dni";
        var hours = minutes / 60;
        var rest = minutes % 60;
        return rest == 0 ? $"{hours} h" : $"{hours} h {rest} min";
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength) return value;
        var cut = maxLength - 1;
        if (char.IsHighSurrogate(value[cut - 1])) cut--;
        return $"{value[..cut]}…";
    }
}
