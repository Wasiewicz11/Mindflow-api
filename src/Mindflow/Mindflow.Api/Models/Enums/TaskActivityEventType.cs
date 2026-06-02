namespace Mindflow.Api.Models.Enums;

public enum TaskActivityEventType
{
    TaskCreated,
    TaskTitleChanged,
    TaskDescriptionChanged,
    TaskDueDateSet,
    TaskDueDateChanged,
    TaskDueDateRemoved,
    TaskTimeSet,
    TaskTimeChanged,
    TaskTimeRemoved,
    TaskCompleted,
    TaskReopened,
    TaskDeleted,
    TaskRestored,
    TaskPostponed,
    TaskOverdue,
    TaskPriorityChanged,
    TaskProjectChanged,
    TaskTagsChanged,
    TaskSubtasksChanged,
    TaskRecurrenceCreated,
    TaskRecurrenceChanged,
    TaskRecurrenceInstanceCreated,
    TaskReminderSent,
    TaskReminderClicked
}
