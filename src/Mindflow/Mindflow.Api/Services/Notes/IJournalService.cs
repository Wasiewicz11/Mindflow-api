using Mindflow.Api.Models.Dtos;

namespace Mindflow.Api.Services.Notes;

public interface IJournalService
{
    Task<IReadOnlyList<JournalDayResponse>> ListAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<NoteResponse?> GetAsync(DateOnly date, CancellationToken ct = default);
    Task<NoteResponse?> SaveAsync(DateOnly date, SaveNoteRequest request, CancellationToken ct = default);
}
