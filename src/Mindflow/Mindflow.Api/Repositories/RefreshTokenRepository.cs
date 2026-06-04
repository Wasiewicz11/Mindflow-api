using Mindflow.Api.Data;
using Mindflow.Api.Models;

namespace Mindflow.Api.Repositories;

public class RefreshTokenRepository(MindflowDbContext dbContext) : IRefreshTokenRepository
{

        public async Task<RefreshToken?> GetByTokenAsync(Guid token)
        {
            return await dbContext.RefreshTokens.FindAsync(token);
        }

        public async Task AddAsync(RefreshToken refreshToken)
        {
            await dbContext.RefreshTokens.AddAsync(refreshToken);
            await dbContext.SaveChangesAsync();
        }

        public async Task RevokeAsync(Guid token)
        {
            var refreshToken = await dbContext.RefreshTokens.FindAsync(token);
            if (refreshToken != null)
            {
                refreshToken.IsRevoked = true;
                await dbContext.SaveChangesAsync();
            }
        }
}