using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Services.Notes;

namespace Mindflow.Api.Controllers;

[ApiController]
[Route("notes")]
[Authorize]
public class NotesController(INoteService notes) : ControllerBase
{
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
}
