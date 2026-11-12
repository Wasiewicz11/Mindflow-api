using Microsoft.Extensions.Configuration;
using Mindflow.Api.Services.Reminders;

namespace Mindflow.Api.Tests.Reminders;

public class ReminderActionTokensTests
{
    private static ReminderActionTokens Create(string secret = "secret-0123456789-0123456789-0123456789") =>
        new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Secret"] = secret }).Build());

    private static readonly Guid TaskId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2030, 1, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Valid_token_round_trips()
    {
        var tokens = Create();
        var claims = tokens.Validate(tokens.Create(TaskId, UserId, Now.AddDays(2)), Now);
        Assert.Equal((TaskId, UserId), (claims!.TaskId, claims.UserId));
    }

    [Fact]
    public void Expired_token_is_rejected()
    {
        var tokens = Create();
        Assert.Null(tokens.Validate(tokens.Create(TaskId, UserId, Now.AddMinutes(-1)), Now));
    }

    [Fact]
    public void Tampered_or_foreign_tokens_are_rejected()
    {
        var tokens = Create();
        var token = tokens.Create(TaskId, UserId, Now.AddDays(2));
        var other = tokens.Create(Guid.NewGuid(), UserId, Now.AddDays(2));
        var swapped = other.Split('.')[0] + "." + token.Split('.')[1];

        Assert.Null(tokens.Validate(swapped, Now));
        var signature = token.Split('.')[1];
        var middle = signature.Length / 2;
        var flipped = signature[..middle] + (signature[middle] == 'A' ? 'B' : 'A') + signature[(middle + 1)..];
        Assert.Null(tokens.Validate(token.Split('.')[0] + "." + flipped, Now));
        Assert.Null(Create("another-secret-0123456789-0123456789").Validate(token, Now));
        Assert.Null(tokens.Validate("garbage", Now));
        Assert.Null(tokens.Validate(null, Now));
    }
}
