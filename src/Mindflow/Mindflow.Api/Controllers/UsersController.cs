using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Mindflow.Api.Services;

namespace Mindflow.Api.Controllers;

[ApiController]
[Route("users")]
[Authorize]
public class UsersController(ICurrentUserService currentUserService, IUserService userService) : ControllerBase
{
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        var user = await userService.GetByIdAsync(userId);
        return Ok(user);
    }
}
