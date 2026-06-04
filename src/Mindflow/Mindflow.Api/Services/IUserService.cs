using Mindflow.Api.Models.Dtos;

namespace Mindflow.Api.Services;

public interface IUserService
{
    Task<UserDto> GetByIdAsync(Guid userId);
}