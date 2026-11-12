using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Data;
using Mindflow.Api.Exceptions;
using Mindflow.Api.Hubs;
using Mindflow.Api.Models;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Services.Ai.Dictation;

namespace Mindflow.Api.Services.Notes;

public class NoteSuggestionService(
    MindflowDbContext db,
    ICurrentUserService currentUser,
    IAccessService access,
    ITaskService taskService,
    ITasksNotifier notifier,
    NoteSuggestionReader reader,
    ILogger<NoteSuggestionService> logger) : INoteSuggestionService
{
    private const int MaxTitle = 200;
    private const int MaxDescription = 10000;
    private const int MaxSubtasks = 20;
    private const int MaxSubtaskLength = 1000;
    private const string HandledMessage = "Ta sugestia jest już obsłużona.";

    public async Task<NoteSuggestionResponse> AcceptAsync(Guid noteId, Guid suggestionId, AcceptNoteSuggestionRequest? request, CancellationToken ct = default)
    {
        var userId = await currentUser.GetCurrentUserIdAsync();
        await EnsureNoteAsync(noteId, userId, ct);
        var suggestion = await AcceptCoreAsync(userId, noteId, suggestionId, request, ct);
        await NotifySafelyAsync(userId, noteId);
        return await ToResponseAsync(userId, suggestion, ct);
    }

    public async Task<NoteSuggestionResponse> RejectAsync(Guid noteId, Guid suggestionId, CancellationToken ct = default) =>
        await MoveAsync(noteId, suggestionId, NoteSuggestionStatus.Pending, NoteSuggestionStatus.Rejected, "Można odrzucić tylko oczekującą sugestię.", ct);

    public async Task<NoteSuggestionResponse> RestoreAsync(Guid noteId, Guid suggestionId, CancellationToken ct = default) =>
        await MoveAsync(noteId, suggestionId, NoteSuggestionStatus.Rejected, NoteSuggestionStatus.Pending, "Można przywrócić tylko odrzuconą sugestię.", ct, restoreUnfinished: true);

    public async Task<NoteResponse> AcceptAllAsync(Guid noteId, CancellationToken ct = default)
    {
        var userId = await currentUser.GetCurrentUserIdAsync();
        var note = await EnsureNoteAsync(noteId, userId, ct);
        var pending = await db.NoteTaskSuggestions.AsNoTracking()
            .Where(s => s.NoteId == noteId && s.Status == NoteSuggestionStatus.Pending)
            .OrderBy(s => s.Order)
            .Select(s => s.Id)
            .ToListAsync(ct);

        foreach (var id in pending)
        {
            try
            {
                await AcceptCoreAsync(userId, noteId, id, null, ct);
            }
            catch (Exception ex) when (ex is ApiException or DbUpdateException)
            {
                logger.LogWarning("Note suggestion was not accepted: {ExceptionType}", ex.GetType().Name);
            }
        }

        await NotifySafelyAsync(userId, noteId);
        return NoteMapper.ToResponse(note, await reader.ReadAsync(userId, noteId, ct));
    }

    private async Task<NoteTaskSuggestion> AcceptCoreAsync(Guid userId, Guid noteId, Guid suggestionId, AcceptNoteSuggestionRequest? request, CancellationToken ct)
    {
        var suggestion = await FindAsync(noteId, suggestionId, ct);
        var claimed = await SetStatusAsync(suggestionId, NoteSuggestionStatus.Pending, NoteSuggestionStatus.Accepted, ct);
        if (claimed == 0) throw new BadRequestException(HandledMessage);

        try
        {
            Apply(suggestion, request);
            if (suggestion.ProjectId is { } projectId && !await access.CanAccessProjectAsync(projectId, userId))
                throw new BadRequestException("Nie masz dostępu do tego projektu.");

            var created = await taskService.CreateAsync(DictatedTaskRequests.ToCreateRequest(ToDictated(suggestion)))
                ?? throw new BadRequestException("Nie udało się dodać zadania.");

            suggestion.TaskId = created.Id;
            suggestion.Status = NoteSuggestionStatus.Accepted;
            await db.SaveChangesAsync(ct);
            return suggestion;
        }
        catch
        {
            db.ChangeTracker.Clear();
            await SetStatusAsync(suggestionId, NoteSuggestionStatus.Accepted, NoteSuggestionStatus.Pending, CancellationToken.None);
            throw;
        }
    }

    private async Task<NoteSuggestionResponse> MoveAsync(Guid noteId, Guid suggestionId, NoteSuggestionStatus from, NoteSuggestionStatus to, string error, CancellationToken ct, bool restoreUnfinished = false)
    {
        var userId = await currentUser.GetCurrentUserIdAsync();
        await EnsureNoteAsync(noteId, userId, ct);
        await FindAsync(noteId, suggestionId, ct);
        var moved = await SetStatusAsync(suggestionId, from, to, ct);
        if (moved == 0 && restoreUnfinished)
            moved = await db.NoteTaskSuggestions
                .Where(s => s.Id == suggestionId && s.Status == NoteSuggestionStatus.Accepted && s.TaskId == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.Status, to), ct);
        if (moved == 0) throw new BadRequestException(error);

        db.ChangeTracker.Clear();
        await NotifySafelyAsync(userId, noteId);
        return await ToResponseAsync(userId, await FindAsync(noteId, suggestionId, ct), ct);
    }

    private static void Apply(NoteTaskSuggestion suggestion, AcceptNoteSuggestionRequest? request)
    {
        if (request is null) return;
        Validate(request);
        if (!string.IsNullOrWhiteSpace(request.Title)) suggestion.Title = request.Title.Trim();
        if (request.Description is not null) suggestion.Description = request.Description;
        if (request.Priority is { } priority) suggestion.Priority = priority;
        if (request.Subtasks is not null) suggestion.Subtasks = request.Subtasks.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList();

        if (request.ClearDueDate)
        {
            suggestion.DueDate = null;
            suggestion.DueTime = null;
        }
        else
        {
            if (request.DueDate is { } date) suggestion.DueDate = date;
            if (request.DueTime is { } time) suggestion.DueTime = time;
        }

        if (suggestion.DueDate is null) suggestion.DueTime = null;

        if (request.ClearProject) suggestion.ProjectId = null;
        else if (request.ProjectId is { } projectId) suggestion.ProjectId = projectId;
    }

    private static void Validate(AcceptNoteSuggestionRequest request)
    {
        if (request.Title is { Length: > MaxTitle }) throw new BadRequestException("Za długi tytuł.");
        if (request.Description is { Length: > MaxDescription }) throw new BadRequestException("Za długi opis.");
        if (request.Subtasks is null) return;
        if (request.Subtasks.Count > MaxSubtasks) throw new BadRequestException("Za dużo podzadań.");
        if (request.Subtasks.Any(s => s is { Length: > MaxSubtaskLength })) throw new BadRequestException("Za długie podzadanie.");
    }

    private static DictatedTaskResponse ToDictated(NoteTaskSuggestion s) =>
        new(s.Title, s.Description, s.DueDate, s.DueTime, s.Priority, s.ProjectId, null, s.Subtasks);

    private Task<int> SetStatusAsync(Guid suggestionId, NoteSuggestionStatus from, NoteSuggestionStatus to, CancellationToken ct) =>
        db.NoteTaskSuggestions
            .Where(s => s.Id == suggestionId && s.Status == from)
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.Status, to), ct);

    private async Task<NoteSuggestionResponse> ToResponseAsync(Guid userId, NoteTaskSuggestion suggestion, CancellationToken ct)
    {
        var names = await reader.ProjectNamesAsync(userId, suggestion.ProjectId is not null, ct);
        return NoteMapper.ToSuggestion(suggestion, suggestion.ProjectId is { } p ? names.GetValueOrDefault(p) : null);
    }

    private async Task<NoteTaskSuggestion> FindAsync(Guid noteId, Guid suggestionId, CancellationToken ct) =>
        await db.NoteTaskSuggestions.FirstOrDefaultAsync(s => s.Id == suggestionId && s.NoteId == noteId, ct)
        ?? throw new NotFoundException("Nie znaleziono sugestii.");

    private async Task<Note> EnsureNoteAsync(Guid noteId, Guid userId, CancellationToken ct) =>
        await db.Notes.AsNoTracking().FirstOrDefaultAsync(n => n.Id == noteId && n.UserId == userId, ct)
        ?? throw new NotFoundException("Nie znaleziono notatki.");

    private async Task NotifySafelyAsync(Guid userId, Guid noteId)
    {
        try
        {
            await notifier.NoteUpdatedAsync(userId, noteId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Note notification failed.");
        }
    }
}
