using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Services.Ai;
using Mindflow.Api.Services.Ai.Dictation;
using Mindflow.Api.Services.Ai.Providers;
using Mindflow.Api.Services.Notes;

namespace Mindflow.Api.Controllers;

[ApiController]
[Route("notes")]
[Authorize]
public class NotesController(INoteService notes, INoteSuggestionService suggestions) : ControllerBase
{
    private const long MaxAudioUploadBytes = 9 * 1024 * 1024;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<NoteListItemResponse>>> List(CancellationToken ct) =>
        Ok(await notes.ListAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<NoteResponse>> Get(Guid id, CancellationToken ct) =>
        Ok(await notes.GetAsync(id, ct));

    [HttpPost]
    public async Task<ActionResult<NoteResponse>> Create(SaveNoteRequest request, CancellationToken ct) =>
        Ok(await notes.CreateAsync(request, ct));

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<NoteResponse>> Update(Guid id, SaveNoteRequest request, CancellationToken ct) =>
        Ok(await notes.UpdateAsync(id, request, ct));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await notes.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpPost("import")]
    public async Task<ActionResult<ImportNotesResponse>> Import(ImportNotesRequest request, CancellationToken ct) =>
        Ok(await notes.ImportAsync(request, ct));

    [HttpPost("{id:guid}/review")]
    public async Task<ActionResult<NoteResponse>> Review(Guid id, CancellationToken ct) =>
        Ok(await notes.MarkReviewedAsync(id, ct));

    [HttpPost("dictation")]
    [Consumes("application/json")]
    [EnableRateLimiting(AiRateLimits.Policy)]
    public async Task<ActionResult<NoteResponse>> DictateText([FromBody] DictationTextRequest request, CancellationToken ct) =>
        Accepted(await notes.DictateAsync(new DictationInput(request.Text, null, null), ct));

    [HttpPost("dictation")]
    [Consumes("multipart/form-data")]
    [EnableRateLimiting(AiRateLimits.Policy)]
    [RequestSizeLimit(MaxAudioUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxAudioUploadBytes)]
    public async Task<ActionResult<NoteResponse>> DictateAudio(IFormFile audio, CancellationToken ct)
    {
        if (audio.Length == 0)
            return BadRequest(new { error = "Nagranie jest puste." });

        await using var stream = audio.OpenReadStream();
        using var buffer = new MemoryStream((int)audio.Length);
        await stream.CopyToAsync(buffer, ct);

        var input = new DictationInput(null, buffer.ToArray(), AudioFormats.Normalize(audio.ContentType));
        return Accepted(await notes.DictateAsync(input, ct));
    }

    [HttpPost("{id:guid}/retry")]
    [EnableRateLimiting(AiRateLimits.Policy)]
    public async Task<ActionResult<NoteResponse>> Retry(Guid id, CancellationToken ct) =>
        Ok(await notes.RetryAsync(id, ct));

    [HttpPost("{id:guid}/suggestions/{suggestionId:guid}/accept")]
    public async Task<ActionResult<NoteSuggestionResponse>> AcceptSuggestion(
        Guid id, Guid suggestionId, [FromBody] AcceptNoteSuggestionRequest? request, CancellationToken ct) =>
        Ok(await suggestions.AcceptAsync(id, suggestionId, request, ct));

    [HttpPost("{id:guid}/suggestions/{suggestionId:guid}/reject")]
    public async Task<ActionResult<NoteSuggestionResponse>> RejectSuggestion(Guid id, Guid suggestionId, CancellationToken ct) =>
        Ok(await suggestions.RejectAsync(id, suggestionId, ct));

    [HttpPost("{id:guid}/suggestions/{suggestionId:guid}/restore")]
    public async Task<ActionResult<NoteSuggestionResponse>> RestoreSuggestion(Guid id, Guid suggestionId, CancellationToken ct) =>
        Ok(await suggestions.RestoreAsync(id, suggestionId, ct));

    [HttpPost("{id:guid}/suggestions/accept-all")]
    public async Task<ActionResult<NoteResponse>> AcceptAllSuggestions(Guid id, CancellationToken ct) =>
        Ok(await suggestions.AcceptAllAsync(id, ct));
}
