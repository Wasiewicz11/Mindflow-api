using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace Mindflow.Api.Models.Dtos;

public record UpsertGoalDayRequest(
    [MaxLength(20)] string DayShort,
    [MaxLength(20)] string DateLabel,
    [MaxLength(255)] string Title,
    [Range(0, 4)] int MarkerLevel,
    JsonElement Sections,
    IReadOnlyCollection<string>? LinkedTaskIds);

public record GoalDayResponse(
    Guid Id,
    DateOnly Date,
    string DayShort,
    string DateLabel,
    string Title,
    int MarkerLevel,
    JsonElement Sections,
    IReadOnlyCollection<string> LinkedTaskIds,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
