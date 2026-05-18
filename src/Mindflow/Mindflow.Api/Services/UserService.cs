using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Data;
using Mindflow.Api.Models.Dtos;

namespace Mindflow.Api.Services;

public class UserService(MindflowDbContext db) : IUserService
{
    public async Task<UserDto> GetByIdAsync(Guid userId)
    {
        var user = await db.Users.FindAsync(userId)
            ?? throw new KeyNotFoundException("User not found.");

        return new UserDto(user.Id, user.Email, user.FirstName, user.LastName, user.AvatarUrl, user.TimeZone);
    }
}
