using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Mindflow.Api.Services;

namespace Mindflow.Api.Controllers;

[ApiController]
[Route("auth")]
public class AuthController(IAuthService authService) : ControllerBase
{
    private const string RefreshTokenCookie = "refresh_token";

    [HttpPost("register")]
    [Authorize(AuthenticationSchemes = "Google")]
    public async Task<IActionResult> Register()
    {
        var sub = GetSub();
        var email = GetEmail();
        var provider = AuthProviderResolver.Resolve(User);

        var (accessToken, refreshToken) = await authService.RegisterAsync(sub, email, provider);

        SetRefreshTokenCookie(refreshToken);
        return Ok(new { accessToken, expiresIn = 900 });
    }

    [HttpPost("login")]
    [Authorize(AuthenticationSchemes = "Google")]
    public async Task<IActionResult> Login()
    {
        var sub = GetSub();
        var provider = AuthProviderResolver.Resolve(User);

        var (accessToken, refreshToken) = await authService.LoginAsync(sub, provider);

        SetRefreshTokenCookie(refreshToken);
        return Ok(new { accessToken, expiresIn = 900 });
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh()
    {
        if (!Request.Cookies.TryGetValue(RefreshTokenCookie, out var raw) || !Guid.TryParse(raw, out var token))
            return Unauthorized();

        var (accessToken, refreshToken) = await authService.RefreshAsync(token);

        SetRefreshTokenCookie(refreshToken);
        return Ok(new { accessToken, expiresIn = 900 });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        if (!Request.Cookies.TryGetValue(RefreshTokenCookie, out var raw) || !Guid.TryParse(raw, out var token))
            return Unauthorized();

        await authService.LogoutAsync(token);

        Response.Cookies.Delete(RefreshTokenCookie);
        return NoContent();
    }

    private string GetSub() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? User.FindFirstValue("sub")
        ?? throw new UnauthorizedAccessException("Missing 'sub' claim.");

    private string GetEmail() =>
        User.FindFirstValue(ClaimTypes.Email)
        ?? User.FindFirstValue("email")
        ?? throw new UnauthorizedAccessException("Missing 'email' claim.");

    private void SetRefreshTokenCookie(Guid token)
    {
        Response.Cookies.Append(RefreshTokenCookie, token.ToString(), new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            MaxAge = TimeSpan.FromDays(30)
        });
    }
}
