using Mindflow.Api.Models;
using Mindflow.Api.Models.Dtos;

namespace Mindflow.Api.Services.Notes;

public static class NoteMapper
{
    public static NoteListItemResponse ToListItem(Note note, int pendingSuggestions = 0) => new(
        note.Id,
        note.Title,
        NoteText.Preview(note.Content),
        note.Tags,
        note.ProjectId,
        note.Source,
        note.Status,
        note.ReviewedAt is null,
        note.EventDate,
        note.CreatedAt,
        note.UpdatedAt,
        pendingSuggestions);

    public static NoteResponse ToResponse(Note note, IReadOnlyList<NoteSuggestionResponse>? suggestions = null) => new(
        note.Id,
        note.Title,
        note.Content,
        NoteText.Preview(note.Content),
        note.Tags,
        note.ProjectId,
        note.Source,
        note.Status,
        note.ReviewedAt is null,
        note.EventDate,
        note.Transcript,
        note.CreatedAt,
        note.UpdatedAt,
        suggestions ?? []);

    public static NoteSuggestionResponse ToSuggestion(NoteTaskSuggestion s, string? projectName) => new(
        s.Id,
        s.Title,
        s.Description,
        s.DueDate,
        s.DueTime,
        s.Priority,
        s.ProjectId,
        projectName,
        s.Subtasks,
        s.Status,
        s.TaskId);
}
