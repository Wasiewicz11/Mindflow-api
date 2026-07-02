using System.Text.Json;
using Mindflow.Api.Exceptions;
using Mindflow.Api.Models;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Repositories;

namespace Mindflow.Api.Services;

public class GoalDayService(
    IGoalDayRepository goalDayRepository,
    ICurrentUserService currentUserService) : IGoalDayService
{
    private const int MaxSectionsJsonLength = 200_000;
    private const int MaxLinkedTaskIds = 200;

    public async Task<IReadOnlyCollection<GoalDayResponse>> GetAllAsync()
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        var days = await goalDayRepository.GetAllForUserAsync(userId);
        return days.Select(ToResponse).ToList();
    }

    public async Task<GoalDayResponse?> GetByDateAsync(DateOnly date)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        var day = await goalDayRepository.GetByDateAsync(userId, date);
        return day is null ? null : ToResponse(day);
    }

    public async Task<GoalDayResponse> UpsertAsync(DateOnly date, UpsertGoalDayRequest request)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        var now = DateTimeOffset.UtcNow;
        var sectionsJson = SerializeSections(request.Sections);
        var linkedTaskIdsJson = JsonSerializer.Serialize(NormalizeLinkedTaskIds(request.LinkedTaskIds));
        var title = NormalizeRequiredString(request.Title, "Title", 255);

        var day = new GoalDay
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Date = date,
            DayShort = NormalizeRequiredString(request.DayShort, "DayShort", 20),
            DateLabel = NormalizeRequiredString(request.DateLabel, "DateLabel", 20),
            Title = title,
            MarkerLevel = request.MarkerLevel,
            SectionsJson = sectionsJson,
            LinkedTaskIdsJson = linkedTaskIdsJson,
            CreatedAt = now,
            UpdatedAt = now
        };

        var saved = await goalDayRepository.UpsertAsync(day);
        return ToResponse(saved);
    }

    public async Task<bool> DeleteAsync(DateOnly date)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        return await goalDayRepository.DeleteAsync(userId, date);
    }

    private static string SerializeSections(JsonElement sections)
    {
        if (sections.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return "[]";

        if (sections.ValueKind != JsonValueKind.Array)
            throw new BadRequestException("Sections must be an array.");

        var json = JsonSerializer.Serialize(sections);
        if (json.Length > MaxSectionsJsonLength)
            throw new BadRequestException("Sections payload is too large.");

        return json;
    }

    private static IReadOnlyCollection<string> NormalizeLinkedTaskIds(IReadOnlyCollection<string>? linkedTaskIds)
    {
        if (linkedTaskIds is null) return Array.Empty<string>();
        if (linkedTaskIds.Count > MaxLinkedTaskIds)
            throw new BadRequestException($"LinkedTaskIds cannot contain more than {MaxLinkedTaskIds} items.");

        return linkedTaskIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Where(id => id.Length <= 128)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizeRequiredString(string? value, string fieldName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new BadRequestException($"{fieldName} is required.");

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
            throw new BadRequestException($"{fieldName} cannot be longer than {maxLength} characters.");

        return trimmed;
    }

    private static GoalDayResponse ToResponse(GoalDay day)
    {
        var sections = JsonSerializer.Deserialize<JsonElement>(day.SectionsJson);
        var linkedTaskIds = JsonSerializer.Deserialize<IReadOnlyCollection<string>>(day.LinkedTaskIdsJson)
            ?? Array.Empty<string>();

        return new GoalDayResponse(
            day.Id,
            day.Date,
            day.DayShort,
            day.DateLabel,
            day.Title,
            day.MarkerLevel,
            sections,
            linkedTaskIds,
            day.CreatedAt,
            day.UpdatedAt);
    }
}
