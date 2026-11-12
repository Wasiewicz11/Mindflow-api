using Mindflow.Api.Models;
using Mindflow.Api.Models.Dtos;

namespace Mindflow.Api.Services.Notes;

public static class NoteMapper
{
    public static NoteListItemResponse ToListItem(Note note) => new(
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
        note.UpdatedAt);

    public static NoteResponse ToResponse(Note note) => new(
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
        note.UpdatedAt);
}
