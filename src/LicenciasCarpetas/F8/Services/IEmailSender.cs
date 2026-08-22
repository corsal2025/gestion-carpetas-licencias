namespace LicenciasCarpetas.F8.Services;

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body,
        IReadOnlyList<EmailAttachment>? attachments = null,
        CancellationToken cancellationToken = default);
}
