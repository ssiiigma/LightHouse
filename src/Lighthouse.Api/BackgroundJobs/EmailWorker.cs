using Lighthouse.Api.Options;
using Lighthouse.Api.Services.Email;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Lighthouse.Api.BackgroundJobs;

/// <summary>Відправляє листи з черги через SMTP (MailKit); до 4 спроб із паузами 2, 10 і 30 с. Тіла листів у журнал не потрапляють.</summary>
public sealed class EmailWorker(EmailQueue queue, IOptions<SmtpOptions> options, ILogger<EmailWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan[] RetryDelays =
        [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var message in queue.Reader.ReadAllAsync(stoppingToken))
            {
                await DeliverAsync(message, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // зупинка застосунку
        }
    }

    private async Task DeliverAsync(EmailMessage message, CancellationToken ct)
    {
        var smtp = options.Value;
        if (string.IsNullOrWhiteSpace(smtp.Host))
        {
            logger.LogWarning("SMTP is not configured (Smtp:Host); the email \"{Subject}\" was not sent.", message.Subject);
            return;
        }

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await SendAsync(message, smtp, ct);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (attempt >= RetryDelays.Length)
                {
                    logger.LogError(ex, "The email \"{Subject}\" could not be delivered after {Attempts} attempts.",
                        message.Subject, attempt + 1);
                    return;
                }

                logger.LogWarning(ex, "Email delivery attempt {Attempt} failed; retrying.", attempt + 1);
                await Task.Delay(RetryDelays[attempt], ct);
            }
        }
    }

    private static async Task SendAsync(EmailMessage email, SmtpOptions smtp, CancellationToken ct)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(smtp.FromName, smtp.FromAddress));
        mime.To.Add(MailboxAddress.Parse(email.To));
        mime.Subject = email.Subject;
        mime.Body = new BodyBuilder { TextBody = email.TextBody, HtmlBody = email.HtmlBody }.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(
            smtp.Host, smtp.Port, smtp.UseTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None, ct);
        if (!string.IsNullOrEmpty(smtp.User))
        {
            await client.AuthenticateAsync(smtp.User, smtp.Password, ct);
        }

        await client.SendAsync(mime, ct);
        await client.DisconnectAsync(true, ct);
    }
}