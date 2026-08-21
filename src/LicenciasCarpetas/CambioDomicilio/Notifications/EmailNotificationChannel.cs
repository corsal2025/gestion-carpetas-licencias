using LicenciasCarpetas.CambioDomicilio;
using LicenciasCarpetas.CambioDomicilio.Ews;

namespace LicenciasCarpetas.CambioDomicilio.Notifications;

/// <summary>Works identically on PC and on a headless VPS — always fired regardless of the toast channel.</summary>
public sealed class EmailNotificationChannel(IMailSender mailSender, CambioDomicilioOptions options) : INotificationChannel
{
    public void NotifyConfirmationSent(string fullName, string rut, string comuna)
    {
        // Sin CambioDomicilio:NotificationEmailAddress configurado, no hay a quién avisar —
        // silencioso, igual que el canal toast cuando está deshabilitado.
        if (string.IsNullOrWhiteSpace(options.NotificationEmailAddress))
        {
            return;
        }

        var (subject, body) = EmailTemplates.ConfirmationSentNotification(fullName, rut, comuna);
        // Fire-and-forget is intentional here: notification delivery must not block or fail the polling cycle.
        _ = mailSender.SendAsync(options.NotificationEmailAddress, subject, body, CancellationToken.None);
    }
}
