using System.ComponentModel.DataAnnotations;
using Mindflow.Api.Models.Enums;

namespace Mindflow.Api.Models.Dtos;

public record AiUsageResponse(int Used, int Limit);

public record AiSettingsResponse(
    AiProvider Provider,
    string Model,
    bool UsesDefaultModel,
    bool HasKey,
    string? KeyLast4,
    bool CanStoreKeys,
    bool ServerKeyAvailable,
    AiProvider? ServerProvider,
    string ActiveSource,
    IReadOnlyDictionary<string, string> DefaultModels,
    AiUsageResponse Dictation,
    AiUsageResponse Suggestions);

public record UpdateAiSettingsRequest(
    AiProvider Provider,
    [MaxLength(300)] string? ApiKey,
    [MaxLength(100)] string? Model);

public record TestAiConnectionRequest(
    AiProvider? Provider,
    [MaxLength(300)] string? ApiKey,
    [MaxLength(100)] string? Model);

public record AiConnectionTestResponse(bool Ok, AiProvider? Provider, string? Model, string Source, string Message);

public record DictationTextRequest([Required, MaxLength(5000)] string Text);

public record DictatedTaskResponse(
    string Title,
    string? Description,
    DateOnly? DueDate,
    TimeOnly? DueTime,
    TaskPriority Priority,
    Guid? ProjectId,
    string? ProjectName,
    IReadOnlyList<string> Subtasks);

public record DictationResponse(
    string Transcript,
    IReadOnlyList<DictatedTaskResponse> Tasks,
    AiProvider Provider,
    string Model,
    string Source,
    AiUsageResponse Usage);
