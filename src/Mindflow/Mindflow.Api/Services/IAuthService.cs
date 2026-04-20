using Mindflow.Api.Models.Enums;

namespace Mindflow.Api.Services;

public interface IAuthService
{
    Task RegisterAsync(string sub, string email, AuthProvider provider);
}
