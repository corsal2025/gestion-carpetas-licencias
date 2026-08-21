namespace LicenciasCarpetas.CambioDomicilio.Notifications;

public interface INotificationChannel
{
    void NotifyConfirmationSent(string fullName, string rut, string comuna);
}
