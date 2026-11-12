using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Mindflow.Api.Data;
using Mindflow.Api.Exceptions;
using Mindflow.Api.Hubs;
using Mindflow.Api.Models;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Services.Ai;
using Mindflow.Api.Services.Ai.Dictation;

namespace Mindflow.Api.Services.Notes;

public class NoteService(
    MindflowDbContext db,
    ICurrentUserService currentUser,
    IAccessService access,
    ITasksNotifier notifier,
    IAiCredentialResolver resolver,
    IDictationQuota quota,
    NoteDictationQueue queue,
    IOptions<AiOptions> aiOptions,
    ILogger<NoteService> logger) : INoteService
{
    private readonly DictationOptions _dictation = aiOptions.Value.Dictation;

    public async Task<IReadOnlyList<NoteListItemResponse>> ListAsync(CancellationToken ct = default)
    {
        var userId = await currentUser.GetCurrentUserIdAsync();
        var notes = await db.Notes.AsNoTracking()
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.UpdatedAt)
            .ToListAsync(ct);
        return notes.Select(NoteMapper.ToListItem).ToList();
    }

    public async Task<NoteResponse> GetAsync(Guid id, CancellationToken ct = default)
    {
        var userId = await currentUser.GetCurrentUserIdAsync();
        return NoteMapper.ToResponse(await FindOwnAsync(id, userId, ct));
    }

    public async Task<NoteResponse> CreateAsync(SaveNoteRequest request, CancellationToken ct = default)
    {
        var userId = await currentUser.GetCurrentUserIdAsync();
        await EnsureProjectAsync(request.ProjectId, userId);
        var now = DateTimeOffset.UtcNow;
        var note = new Note
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = request.Title?.Trim() ?? "",
            Content = request.Content ?? "",
            Tags = NoteText.NormalizeTags(request.Tags),
            ProjectId = request.ProjectId,
            Source = NoteSource.App,
            Status = NoteStatus.Ready,
            ReviewedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Notes.Add(note);
        await db.SaveChangesAsync(ct);
        await NotifySafelyAsync(() => notifier.NoteCreatedAsync(userId, note.Id));
        return NoteMapper.ToResponse(note);
    }

    public async Task<NoteResponse> UpdateAsync(Guid id, SaveNoteRequest request, CancellationToken ct = default)
    {
        var userId = await currentUser.GetCurrentUserIdAsync();
        var note = await FindOwnAsync(id, userId, ct);
        if (note.Status == NoteStatus.Processing) throw new BadRequestException("Notatka jest jeszcze przetwarzana.");
        if (request.ProjectId != note.ProjectId) await EnsureProjectAsync(request.ProjectId, userId);

        note.Title = request.Title?.Trim() ?? "";
        note.Content = request.Content ?? "";
        note.Tags = NoteText.NormalizeTags(request.Tags);
        note.ProjectId = request.ProjectId;
        note.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await NotifySafelyAsync(() => notifier.NoteUpdatedAsync(userId, note.Id));
        return NoteMapper.ToResponse(note);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var userId = await currentUser.GetCurrentUserIdAsync();
        var note = await FindOwnAsync(id, userId, ct);
        db.Notes.Remove(note);
        await db.SaveChangesAsync(ct);
        await NotifySafelyAsync(() => notifier.NoteDeletedAsync(userId, id));
    }

    public async Task<ImportNotesResponse> ImportAsync(ImportNotesRequest request, CancellationToken ct = default)
    {
        var userId = await currentUser.GetCurrentUserIdAsync();
        var clientIds = request.Notes.Select(n => n.ClientId).ToList();
        var known = (await db.Notes
                .Where(n => n.UserId == userId && n.ClientId != null && clientIds.Contains(n.ClientId))
                .Select(n => n.ClientId!)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

        var now = DateTimeOffset.UtcNow;
        var imported = 0;
        foreach (var item in request.Notes)
        {
            if (!known.Add(item.ClientId)) continue;
            var projectId = item.ProjectId is { } p && await access.CanAccessProjectAsync(p, userId) ? p : (Guid?)null;
            var createdAt = item.CreatedAt is { } c && c < now ? c.ToUniversalTime() : now;
            db.Notes.Add(new Note
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                ClientId = item.ClientId,
                Title = item.Title?.Trim() ?? "",
                Content = item.Content ?? "",
                Tags = NoteText.NormalizeTags(item.Tags),
                ProjectId = projectId,
                Source = string.Equals(item.Source, "telegram", StringComparison.OrdinalIgnoreCase) ? NoteSource.Telegram : NoteSource.App,
                Status = NoteStatus.Ready,
                ReviewedAt = createdAt,
                CreatedAt = createdAt,
                UpdatedAt = createdAt
            });
            imported++;
        }

        if (imported > 0) await db.SaveChangesAsync(ct);
        return new ImportNotesResponse(imported, request.Notes.Count - imported);
    }

    public async Task<NoteResponse> MarkReviewedAsync(Guid id, CancellationToken ct = default)
    {
        var userId = await currentUser.GetCurrentUserIdAsync();
        var note = await FindOwnAsync(id, userId, ct);
        if (note.ReviewedAt is null)
        {
            note.ReviewedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            await NotifySafelyAsync(() => notifier.NoteUpdatedAsync(userId, note.Id));
        }
        return NoteMapper.ToResponse(note);
    }

    public async Task<NoteResponse> DictateAsync(DictationInput input, CancellationToken ct = default)
    {
        var userId = await currentUser.GetCurrentUserIdAsync();
        DictationInputRules.Validate(input, _dictation);
        var credentials = (await resolver.ResolveAsync(userId, ct)).FirstOrDefault()
            ?? throw new ServiceUnavailableException("AI nie jest skonfigurowane. Dodaj klucz w Ustawieniach → AI.");
        var reservation = await quota.ReserveAsync(userId, credentials, ct);

        var now = DateTimeOffset.UtcNow;
        var note = new Note
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = "Nowa notatka",
            Content = "",
            Source = NoteSource.Dictation,
            Status = NoteStatus.Processing,
            ReviewedAt = null,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Notes.Add(note);
        db.NoteDictationJobs.Add(new NoteDictationJob
        {
            NoteId = note.Id,
            UserId = userId,
            Audio = input.IsAudio ? input.Audio : null,
            AudioMimeType = input.IsAudio ? input.AudioMimeType : null,
            Text = input.IsAudio ? null : input.Text!.Trim(),
            Lite = reservation.Lite,
            CreatedAt = now
        });
        await db.SaveChangesAsync(ct);
        await NotifySafelyAsync(() => notifier.NoteCreatedAsync(userId, note.Id));
        queue.Enqueue(note.Id);
        return NoteMapper.ToResponse(note);
    }

    public async Task<NoteResponse> RetryAsync(Guid id, CancellationToken ct = default)
    {
        var userId = await currentUser.GetCurrentUserIdAsync();
        var note = await FindOwnAsync(id, userId, ct);
        var attempts = await db.NoteDictationJobs.Where(j => j.NoteId == id).Select(j => (int?)j.Attempts).FirstOrDefaultAsync(ct);
        if (note.Status != NoteStatus.Failed || attempts is null or >= NoteDictationProcessor.MaxAttempts)
            throw new BadRequestException("Tej notatki nie można już przetworzyć ponownie.");

        note.Status = NoteStatus.Processing;
        await db.SaveChangesAsync(ct);
        await NotifySafelyAsync(() => notifier.NoteUpdatedAsync(userId, note.Id));
        queue.Enqueue(note.Id);
        return NoteMapper.ToResponse(note);
    }

    private async Task<Note> FindOwnAsync(Guid id, Guid userId, CancellationToken ct) =>
        await db.Notes.FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId, ct)
        ?? throw new NotFoundException("Nie znaleziono notatki.");

    private async Task EnsureProjectAsync(Guid? projectId, Guid userId)
    {
        if (projectId is { } id && !await access.CanAccessProjectAsync(id, userId))
            throw new BadRequestException("Nie masz dostępu do tego projektu.");
    }

    private async Task NotifySafelyAsync(Func<Task> send)
    {
        try
        {
            await send();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Note notification failed.");
        }
    }
}
