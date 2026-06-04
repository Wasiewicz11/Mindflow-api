namespace Mindflow.Api.Models.Dtos;

public record CreateSpaceRequest(string Name, string? Color);

public record UpdateSpaceRequest(string? Name, string? Color);
