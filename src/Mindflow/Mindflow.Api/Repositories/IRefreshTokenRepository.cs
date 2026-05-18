using Mindflow.Api.Models;

namespace Mindflow.Api.Repositories;

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> GetByTokenAsync(Guid token);
    Task AddAsync(RefreshToken refreshToken);
    Task RevokeAsync(Guid token);
}
