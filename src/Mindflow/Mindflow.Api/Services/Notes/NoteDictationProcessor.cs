using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Mindflow.Api.Data;
using Mindflow.Api.Exceptions;
using Mindflow.Api.Hubs;
using Mindflow.Api.Models;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Services.Ai;
using Mindflow.Api.Services.Ai.Dictation;
using Mindflow.Api.Services.Ai.Providers;

namespace Mindflow.Api.Services.Notes;

/// <summary>Turns one queued recording or text into a ready note with suggested tasks.</summary>
public class NoteDictationProcessor(
    MindflowDbContext db,
    IAiCredentialResolver resolver,
    IEnumerable<IDictationEngine> engines,
    IDictationContextBuilder contextBuilder,
    ITasksNotifier notifier,
    IOptions<AiOptions> options,
    ILogger<NoteDictationProcessor> logger)
{
    public const int MaxAttempts = 3;
    public const string LiteNotice = "> Tryb Lite: dzienny limit lepszego modelu jest wykorzystany, więc notatka może być mniej dokładna.\n\n";

    private readonly DictationOptions _options = options.Value.Dictation;

    public async Task ProcessAsync(Guid noteId, CancellationToken ct)
    {
        var job = await db.NoteDictationJobs.FirstOrDefaultAsync(j => j.NoteId == noteId, ct);
        var note = job is null ? null : await db.Notes.FirstOrDefaultAsync(n => n.Id == noteId, ct);
        if (job is null || note is null || note.Status != NoteStatus.Processing) return;

        if (job.Attempts >= MaxAttempts)
        {
            await FailAsync(note, job, ct);
            return;
        }

        job.Attempts++;
        await db.SaveChangesAsync(ct);

        var input = new DictationInput(job.Text, job.Audio, job.AudioMimeType);
        NoteDictationResult result;
        string? transcript;
        try
        {
            (result, transcript) = await RunAsync(job, input, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Dictated note {NoteId} failed on attempt {Attempt}.", noteId, job.Attempts);
            await FailAsync(note, job, ct);
            return;
        }

        try
        {
            await CompleteAsync(note, job, result, transcript, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Dictated note {NoteId} could not be saved on attempt {Attempt}.", noteId, job.Attempts);
            db.ChangeTracker.Clear();
            job = await db.NoteDictationJobs.FirstOrDefaultAsync(j => j.NoteId == noteId, ct);
            note = job is null ? null : await db.Notes.FirstOrDefaultAsync(n => n.Id == noteId, ct);
            if (job is not null && note is not null) await FailAsync(note, job, ct);
            return;
        }
        await NotifySafelyAsync(note);
    }

    private async Task CompleteAsync(Note note, NoteDictationJob job, NoteDictationResult result, string? transcript, CancellationToken ct)
    {
        var projectIds = result.Tasks.Where(t => t.ProjectId is not null).Select(t => t.ProjectId!.Value).Distinct().ToList();
        var existingProjects = projectIds.Count == 0
            ? []
            : (await db.Projects.Where(p => projectIds.Contains(p.Id)).Select(p => p.Id).ToListAsync(ct)).ToHashSet();

        var now = DateTimeOffset.UtcNow;
        var content = (job.Lite ? LiteNotice : "") + result.Markdown;
        note.Title = result.Title;
        note.Content = content.Length <= NoteDictationResultParser.MaxContent ? content : content[..NoteDictationResultParser.MaxContent];
        note.Tags = result.Tags;
        note.EventDate = result.EventDate;
        note.Transcript = string.IsNullOrWhiteSpace(transcript) ? null : transcript;
        note.Status = NoteStatus.Ready;
        note.UpdatedAt = now;
        db.NoteTaskSuggestions.AddRange(result.Tasks.Select((task, index) => new NoteTaskSuggestion
        {
            Id = Guid.NewGuid(),
            NoteId = note.Id,
            Order = index,
            Title = task.Title.Length <= NoteDictationResultParser.MaxTitle ? task.Title : task.Title[..NoteDictationResultParser.MaxTitle],
            Description = task.Description,
            DueDate = task.DueDate,
            DueTime = task.DueTime,
            Priority = task.Priority,
            ProjectId = task.ProjectId is { } projectId && existingProjects.Contains(projectId) ? projectId : null,
            Subtasks = task.Subtasks.ToList(),
            Status = NoteSuggestionStatus.Pending,
            CreatedAt = now
        }));
        db.NoteDictationJobs.Remove(job);
        await db.SaveChangesAsync(ct);
    }

    private async Task FailAsync(Note note, NoteDictationJob job, CancellationToken ct)
    {
        note.Status = NoteStatus.Failed;
        if (job.Text is not null) note.Transcript ??= job.Text;
        note.UpdatedAt = DateTimeOffset.UtcNow;
        if (job.Attempts >= MaxAttempts) job.Audio = null;
        await db.SaveChangesAsync(ct);
        await NotifySafelyAsync(note);
    }

    private async Task<(NoteDictationResult Result, string? Transcript)> RunAsync(NoteDictationJob job, DictationInput input, CancellationToken ct)
    {
        var credentials = (await resolver.ResolveAsync(job.UserId, ct)).FirstOrDefault()
            ?? throw new ServiceUnavailableException("AI nie jest skonfigurowane. Dodaj klucz w Ustawieniach → AI.");
        if (job.Lite && credentials.Provider == AiProvider.Gemini && !string.IsNullOrWhiteSpace(_options.GeminiFallbackModel))
            credentials = credentials with { Model = _options.GeminiFallbackModel };

        var context = await contextBuilder.BuildAsync(job.UserId, ct);
        var engine = engines.FirstOrDefault(e => e.Provider == credentials.Provider)
            ?? throw new ServiceUnavailableException("Ten dostawca AI nie obsługuje dyktowania.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        var output = await engine.RunAsync(credentials, input, context, NoteDictationPrompts.Instance, timeout.Token);

        var result = NoteDictationResultParser.Parse(output.Json, context, _options.MaxTasks)
            ?? throw AiProviderErrors.EmptyResponse();
        logger.LogInformation("Dictated note {NoteId} via {Provider}/{Model} (lite: {Lite}) produced {Count} suggestions.",
            job.NoteId, credentials.Provider, output.Model, job.Lite, result.Tasks.Count);
        return (result, input.IsAudio ? output.Transcript?.Replace("\0", "") ?? result.Transcript : job.Text);
    }

    private async Task NotifySafelyAsync(Note note)
    {
        try
        {
            await notifier.NoteUpdatedAsync(note.UserId, note.Id);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Note notification failed.");
        }
    }
}
