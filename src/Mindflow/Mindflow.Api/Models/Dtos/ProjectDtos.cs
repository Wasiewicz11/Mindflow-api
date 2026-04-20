namespace Mindflow.Api.Models.Dtos;

public record CreateProjectRequest(string Name, string? Color);

public record UpdateProjectRequest(string? Name, string? Color);
