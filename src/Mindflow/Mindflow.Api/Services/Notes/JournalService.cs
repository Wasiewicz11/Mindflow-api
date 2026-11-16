using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Data;
using Mindflow.Api.Exceptions;
using Mindflow.Api.Hubs;
using Mindflow.Api.Models;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Models.Enums;
using Npgsql;

namespace Mindflow.Api.Services.Notes;

/// <summary>One journal note per user and day; an empty day has no note.</summary>
public class JournalService(
    MindflowDbContext db,
    ICurrentUserService currentUser,
    ITasksNotifier notifier,
    ILogger<JournalService> logger) : IJournalService
{
    public const int MaxRangeDays = 366;

    public async Task<IReadOnlyList<JournalDayResponse>> ListAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (to < from || to.DayNumber - from.DayNumber >= MaxRangeDays)
            throw new BadRequestException($"Zakres dziennika może mieć najwyżej {MaxRangeDays} dni.");

        var userId = await currentUser.GetCurrentUserIdAsync();
        var notes = await db.Notes.AsNoTracking()
            .Where(n => n.UserId == userId && n.JournalDate >= from && n.JournalDate <= to)
            .OrderBy(n => n.JournalDate)
            .ToListAsync(ct);
        return notes.Select(n => new JournalDayResponse(n.JournalDate!.Value, n.Id, NoteText.Preview(n.Content), n.UpdatedAt)).ToList();
    }

    public async Task<NoteResponse?> GetAsync(DateOnly date, CancellationToken ct = default)
    {
        var userId = await currentUser.GetCurrentUserIdAsync();
        var note = await FindAsync(userId, date, ct);
        return note is null ? null : NoteMapper.ToResponse(note);
    }

    public async Task<NoteResponse?> SaveAsync(DateOnly date, SaveNoteRequest request, CancellationToken ct = default)
    {
        var userId = await currentUser.GetCurrentUserIdAsync();
        var note = await FindAsync(userId, date, ct);
        var empty = string.IsNullOrWhiteSpace(request.Content) && string.IsNullOrWhiteSpace(request.Title);

        if (empty)
        {
            if (note is not null) await DeleteAsync(note, userId, ct);
            return null;
        }

        if (note is null)
        {
            note = await CreateAsync(userId, date, request, ct);
            if (note is not null)
            {
                await NotifySafelyAsync(() => notifier.NoteCreatedAsync(userId, note.Id));
                return NoteMapper.ToResponse(note);
            }
            note = await FindAsync(userId, date, ct) ?? throw new NotFoundException("Nie znaleziono notatki dziennika.");
        }

        Apply(note, request);
        note.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await NotifySafelyAsync(() => notifier.NoteUpdatedAsync(userId, note.Id));
        return NoteMapper.ToResponse(note);
    }

    private async Task<Note?> CreateAsync(Guid userId, DateOnly date, SaveNoteRequest request, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var note = new Note
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            JournalDate = date,
            Source = NoteSource.App,
            Status = NoteStatus.Ready,
            ReviewedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
        Apply(note, request);
        db.Notes.Add(note);
        try
        {
            await db.SaveChangesAsync(ct);
            return note;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.Entry(note).State = EntityState.Detached;
            return null;
        }
    }

    private async Task DeleteAsync(Note note, Guid userId, CancellationToken ct)
    {
        db.Notes.Remove(note);
        await db.SaveChangesAsync(ct);
        await NotifySafelyAsync(() => notifier.NoteDeletedAsync(userId, note.Id));
    }

    private static void Apply(Note note, SaveNoteRequest request)
    {
        note.Title = request.Title?.Trim() ?? "";
        note.Content = request.Content ?? "";
        note.Tags = NoteText.NormalizeTags(request.Tags);
    }

    private Task<Note?> FindAsync(Guid userId, DateOnly date, CancellationToken ct) =>
        db.Notes.FirstOrDefaultAsync(n => n.UserId == userId && n.JournalDate == date, ct);

    private async Task NotifySafelyAsync(Func<Task> send)
    {
        try
        {
            await send();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Journal notification failed.");
        }
    }
}
