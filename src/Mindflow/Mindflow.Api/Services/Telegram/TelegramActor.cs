using System.Security.Claims;

namespace Mindflow.Api.Services.Telegram;

public static class TelegramActor
{
    public static void ActAs(IHttpContextAccessor accessor, Guid userId)
    {
        var context = accessor.HttpContext ??= new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Telegram"));
    }
}
