using Mindflow.Api.Models.Dtos;
using Microsoft.AspNetCore.Http;

namespace Mindflow.Api.Services;

public interface IUserService
{
    Task<UserDto> GetByIdAsync(Guid userId);
    Task<UserDto> UploadAvatarAsync(Guid userId, IFormFile? file);
}
