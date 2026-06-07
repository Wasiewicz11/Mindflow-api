using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Mindflow.Api.Services;

namespace Mindflow.Api.Controllers;

[ApiController]
[Route("users")]
[Authorize]
public class UsersController(ICurrentUserService currentUserService, IUserService userService) : ControllerBase
{
    private const long MaxAvatarBytes = 25 * 1024 * 1024;

    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        var user = await userService.GetByIdAsync(userId);
        return Ok(user);
    }

    [HttpPost("me/avatar")]
    [RequestSizeLimit(MaxAvatarBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxAvatarBytes)]
    public async Task<IActionResult> UploadAvatar([FromForm] IFormFile? file)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        var user = await userService.UploadAvatarAsync(userId, file);
        return Ok(user);
    }
}
