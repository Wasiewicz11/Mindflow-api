using System.ComponentModel.DataAnnotations;
using Mindflow.Api.Models.Enums;

namespace Mindflow.Api.Models.Dtos;

public record NoteListItemResponse(
    Guid Id,
    string Title,
    string Preview,
    IReadOnlyList<string> Tags,
    Guid? ProjectId,
    NoteSource Source,
    NoteStatus Status,
    bool IsNew,
    DateOnly? EventDate,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record NoteResponse(
    Guid Id,
    string Title,
    string Content,
    string Preview,
    IReadOnlyList<string> Tags,
    Guid? ProjectId,
    NoteSource Source,
    NoteStatus Status,
    bool IsNew,
    DateOnly? EventDate,
    string? Transcript,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record SaveNoteRequest(
    [MaxLength(200)] string? Title,
    [MaxLength(100_000)] string? Content,
    IReadOnlyList<string>? Tags,
    Guid? ProjectId);

public record ImportNoteItem(
    [Required, MaxLength(64)] string ClientId,
    [MaxLength(200)] string? Title,
    [MaxLength(100_000)] string? Content,
    IReadOnlyList<string>? Tags,
    string? Source,
    Guid? ProjectId,
    DateTimeOffset? CreatedAt);

public record ImportNotesRequest([Required, MaxLength(500)] IReadOnlyList<ImportNoteItem> Notes);

public record ImportNotesResponse(int Imported, int Skipped);
