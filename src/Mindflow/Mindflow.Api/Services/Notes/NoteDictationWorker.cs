using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Data;
using Mindflow.Api.Models.Enums;

namespace Mindflow.Api.Services.Notes;

/// <summary>Processes dictated notes one at a time and resumes the ones left unfinished by a restart.</summary>
public class NoteDictationWorker(
    NoteDictationQueue queue,
    IServiceScopeFactory scopes,
    ILogger<NoteDictationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await EnqueuePendingAsync(stoppingToken);
            await foreach (var noteId in queue.ReadAllAsync(stoppingToken))
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<NoteDictationProcessor>().ProcessAsync(noteId, stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(ex, "Dictated note {NoteId} could not be processed.", noteId);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task EnqueuePendingAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MindflowDbContext>();
            var pending = await db.NoteDictationJobs
                .Where(j => db.Notes.Any(n => n.Id == j.NoteId && n.Status == NoteStatus.Processing))
                .OrderBy(j => j.CreatedAt)
                .Select(j => j.NoteId)
                .ToListAsync(ct);
            foreach (var noteId in pending) queue.Enqueue(noteId);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Pending dictated notes could not be loaded.");
        }
    }
}
