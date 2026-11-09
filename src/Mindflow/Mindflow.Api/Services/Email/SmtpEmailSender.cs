using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Mindflow.Api.Services.Email;

public class SmtpEmailSender(IConfiguration configuration, TimeSpan? sendTimeout = null) : IEmailSender
{
    private readonly TimeSpan totalTimeout = sendTimeout ?? TimeSpan.FromSeconds(30);

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
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(totalTimeout);
        var security = !options.UseSsl
            ? SecureSocketOptions.None
            : options.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
        try
        {
            await client.ConnectAsync(options.Host, options.Port, security, timeout.Token);
            if (!string.IsNullOrWhiteSpace(options.User))
                await client.AuthenticateAsync(options.User, options.Password ?? "", timeout.Token);
            await client.SendAsync(mime, timeout.Token);
            await client.DisconnectAsync(true, timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException("SMTP send timed out.");
        }
    }
}
