using Mindflow.Api.Models.Enums;

namespace Mindflow.Api.Services;

public interface IAuthService
{
    Task<(string AccessToken, Guid RefreshToken)> RegisterAsync(
        string sub, string email, string firstName, string lastName,
        string? googleAvatarUrl, AuthProvider provider);
    Task<(string AccessToken, Guid RefreshToken)> LoginAsync(string sub, AuthProvider provider);
    Task LogoutAsync(Guid refreshToken);
    Task<(string AccessToken, Guid RefreshToken)> RefreshAsync(Guid refreshToken);
}
