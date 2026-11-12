using Mindflow.Api.Models.Dtos;

namespace Mindflow.Api.Services.Notes;

public interface INoteSuggestionService
{
    Task<NoteSuggestionResponse> AcceptAsync(Guid noteId, Guid suggestionId, AcceptNoteSuggestionRequest? request, CancellationToken ct = default);
    Task<NoteSuggestionResponse> RejectAsync(Guid noteId, Guid suggestionId, CancellationToken ct = default);
    Task<NoteSuggestionResponse> RestoreAsync(Guid noteId, Guid suggestionId, CancellationToken ct = default);
    Task<NoteResponse> AcceptAllAsync(Guid noteId, CancellationToken ct = default);
}
