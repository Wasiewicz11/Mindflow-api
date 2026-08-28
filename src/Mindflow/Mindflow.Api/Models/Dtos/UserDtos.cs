namespace Mindflow.Api.Models.Dtos;

public record UserDto(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string? AvatarUrl,
    string TimeZone,
    bool IntegrationsEnabled
);
