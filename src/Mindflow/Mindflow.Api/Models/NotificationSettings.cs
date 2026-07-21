namespace Mindflow.Api.Models;

public class NotificationSettings
{
    public Guid UserId { get; set; }
    public bool Enabled { get; set; } = true;
    public bool MorningBriefEnabled { get; set; } = true;
    public TimeOnly MorningBriefTime { get; set; } = new(6, 0);
    public bool MiddayBriefEnabled { get; set; } = true;
    public TimeOnly MiddayBriefTime { get; set; } = new(13, 0);
    public bool EveningSummaryEnabled { get; set; } = true;
    public TimeOnly EveningSummaryTime { get; set; } = new(20, 0);
    public bool BlockRemindersEnabled { get; set; } = true;
    public int BlockReminderMinutes { get; set; } = 10;
    public DateTimeOffset UpdatedAt { get; set; }
}
