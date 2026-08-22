using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace LicenciasCarpetas.F8.Services;

public sealed class SmtpEmailSender(IOptions<SmtpOptions> options) : IEmailSender
{
    public async Task SendAsync(string to, string subject, string body,
        IReadOnlyList<EmailAttachment>? attachments = null,
        CancellationToken cancellationToken = default)
    {
        var smtp = options.Value;

        using var client = new SmtpClient(smtp.Host, smtp.Port)
        {
            EnableSsl = smtp.EnableSsl,
            Credentials = new NetworkCredential(smtp.Username, smtp.Password),
        };

        using var message = new MailMessage
        {
            From = new MailAddress(smtp.FromAddress, smtp.FromDisplayName),
            Subject = subject,
            Body = body,
        };
        message.To.Add(to);

        if (attachments is not null)
        {
            foreach (var attachment in attachments)
            {
                message.Attachments.Add(new Attachment(attachment.FilePath, attachment.ContentType));
            }
        }

        await client.SendMailAsync(message, cancellationToken);
    }
}
