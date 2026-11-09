using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Mindflow.Api.Services.Email;

public class SmtpEmailSender(IConfiguration configuration) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        var options = EmailOptions.FromConfiguration(configuration)
            ?? throw new InvalidOperationException("SMTP is not configured.");
        var mime = new MimeMessage();
        mime.From.Add(MailboxAddress.Parse(options.From));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { HtmlBody = message.Html, TextBody = message.Text }.ToMessageBody();

        using var client = new SmtpClient { Timeout = 10_000 };
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));
        ct = cts.Token;
        var security = !options.UseSsl
            ? SecureSocketOptions.None
            : options.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
        await client.ConnectAsync(options.Host, options.Port, security, ct);
        if (!string.IsNullOrWhiteSpace(options.User))
            await client.AuthenticateAsync(options.User, options.Password ?? "", ct);
        await client.SendAsync(mime, ct);
        await client.DisconnectAsync(true, ct);
    }
}
