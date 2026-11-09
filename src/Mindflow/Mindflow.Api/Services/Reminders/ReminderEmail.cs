using System.Net;
using System.Text.RegularExpressions;
using Mindflow.Api.Services.Email;

namespace Mindflow.Api.Services.Reminders;

public static class ReminderEmail
{
    public static EmailMessage Build(ReminderMessage message, string actionPageUrl, string to)
    {
        var rawTitle = message.Title.StartsWith("⏰ ") ? message.Title[2..] : message.Title;
        var taskTitle = Regex.Replace(rawTitle, @"\s+", " ").Trim();
        var time = message.LocalDue.Length >= 5 ? message.LocalDue[^5..] : message.LocalDue;
        var done = $"{actionPageUrl}&a=done";
        var snooze = $"{actionPageUrl}&a=snooze";

        var html = $"""
            <!doctype html>
            <html lang="pl"><body style="margin:0;padding:24px;background:#f4f4f5;font-family:-apple-system,Segoe UI,Roboto,Arial,sans-serif;color:#111">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr><td align="center">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:480px;background:#fff;border-radius:16px;border:2px solid #111;padding:28px">
            <tr><td style="font-size:13px;letter-spacing:.08em;text-transform:uppercase;color:#b91c1c;font-weight:700">⏰ Przypomnienie</td></tr>
            <tr><td style="padding-top:10px;font-size:24px;line-height:1.25;font-weight:700">{WebUtility.HtmlEncode(taskTitle)}</td></tr>
            <tr><td style="padding-top:14px;font-size:40px;font-weight:800;letter-spacing:-.02em">{WebUtility.HtmlEncode(time)}</td></tr>
            <tr><td style="padding-top:4px;font-size:15px;color:#52525b">{WebUtility.HtmlEncode(message.Body)}</td></tr>
            <tr><td style="padding-top:24px">
              <a href="{WebUtility.HtmlEncode(done)}" style="display:inline-block;background:#111;color:#fff;text-decoration:none;font-weight:700;padding:12px 18px;border-radius:10px;margin:0 8px 8px 0">Zrobione</a>
              <a href="{WebUtility.HtmlEncode(snooze)}" style="display:inline-block;background:#fff;color:#111;text-decoration:none;font-weight:700;padding:10px 16px;border-radius:10px;border:2px solid #111;margin:0 8px 8px 0">Odłóż 10 min</a>
              <a href="{WebUtility.HtmlEncode(message.TaskUrl)}" style="display:inline-block;color:#111;font-weight:600;padding:12px 4px">Otwórz zadanie →</a>
            </td></tr>
            </table></td></tr></table></body></html>
            """;
        var text = $"""
            ⏰ Przypomnienie: {taskTitle}
            {message.Body}

            Zrobione: {done}
            Odłóż 10 min: {snooze}
            Otwórz zadanie: {message.TaskUrl}
            """;

        return new EmailMessage(to, $"⏰ {taskTitle} o {time}", html, text);
    }
}
