using Mindflow.Api.Models.Dtos;

namespace Mindflow.Api.Services.Notes;

public interface INoteService
{
    Task<IReadOnlyList<NoteListItemResponse>> ListAsync(CancellationToken ct = default);
    Task<NoteResponse> GetAsync(Guid id, CancellationToken ct = default);
    Task<NoteResponse> CreateAsync(SaveNoteRequest request, CancellationToken ct = default);
    Task<NoteResponse> UpdateAsync(Guid id, SaveNoteRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<ImportNotesResponse> ImportAsync(ImportNotesRequest request, CancellationToken ct = default);
    Task<NoteResponse> MarkReviewedAsync(Guid id, CancellationToken ct = default);
}
