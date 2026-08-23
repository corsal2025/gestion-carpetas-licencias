using LicenciasCarpetas.CambioDomicilio.Data;
using LicenciasCarpetas.CambioDomicilio.Domain;
using LicenciasCarpetas.F8.Services;
using LicenciasCarpetas.Persistence;

namespace LicenciasCarpetas.CambioDomicilio.Solicitar;

public enum OutboundSendOutcome
{
    /// <summary>Email sent and the request transitioned Borrador → Enviada.</summary>
    Sent,

    /// <summary>Email sent, but the request already figured as Enviada (another tab/operator got
    /// there first) — not an error, just nothing left to mark.</summary>
    AlreadySent,

    /// <summary>No comuna contact is registered for the request's destination — nothing was sent.</summary>
    NoContact,

    /// <summary>The SMTP send itself failed — the request stays Borrador so the operator can retry.</summary>
    SendFailed
}

public sealed record OutboundSendResult(OutboundSendOutcome Outcome, string DestinationComuna);

/// <summary>Everything shared by every "send this outbound Cambio de Domicilio request" flow:
/// comuna-contact lookup, subject/body construction, the SMTP send with its failure handling, and
/// MarkSent — used by both NuevaModel.OnPostEnviar (the full form) and
/// IndexModel.OnPostSolicitarCambioDomicilio (the one-click Casos button), which otherwise
/// duplicated this ~40-line sequence with slightly different bugs.</summary>
public sealed class OutboundRequestSender(
    IOutboundAddressChangeRequestRepository repository,
    IComunaContactRepository comunaContactRepository,
    IEmailSender emailSender)
{
    public async Task<OutboundSendResult> SendAsync(
        OutboundAddressChangeRequest request,
        IReadOnlyList<OutboundAddressChangeAttachment> attachments,
        long userId,
        CancellationToken cancellationToken = default)
    {
        var contacts = comunaContactRepository.All()
            .Where(c => string.Equals(c.Comuna, request.DestinationComuna, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (contacts.Count == 0)
        {
            return new OutboundSendResult(OutboundSendOutcome.NoContact, request.DestinationComuna);
        }

        var subject = $"Solicitud de cambio de domicilio — {request.FullName} ({request.Rut})";
        var body = BuildBody(request);
        var emailAttachments = attachments
            .Select(a => new EmailAttachment(a.FileName, a.ContentType, a.StoredPath))
            .ToList();
        var to = string.Join(";", contacts.Select(c => c.Email));

        try
        {
            await emailSender.SendAsync(to, subject, body, emailAttachments, cancellationToken);
        }
        catch (Exception ex) when (ex is System.Net.Mail.SmtpException or System.Net.Sockets.SocketException or InvalidOperationException)
        {
            Console.WriteLine($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] Envío de solicitud de cambio de domicilio #{request.Id} a comuna '{request.DestinationComuna}' falló (SMTP).");
            return new OutboundSendResult(OutboundSendOutcome.SendFailed, request.DestinationComuna);
        }

        var sent = repository.MarkSent(request.Id, DateTimeOffset.UtcNow, userId);
        return new OutboundSendResult(
            sent ? OutboundSendOutcome.Sent : OutboundSendOutcome.AlreadySent,
            request.DestinationComuna);
    }

    /// <summary>Texto fijo pedido por el operador, con cita legal del artículo 14 del Decreto 170
    /// (MTT) — Nombre y RUT son los únicos datos que cambian según de quién se esté pidiendo la
    /// carpeta. Calle/Número/Depto ya no se piden en ningún formulario de esta pantalla (se sacaron
    /// de Nueva.cshtml), así que el cuerpo no los necesita.</summary>
    private static string BuildBody(OutboundAddressChangeRequest request)
    {
        var lines = new List<string>
        {
            "Estimados,",
            "",
            "Junto con saludar, y conforme a lo establecido en el artículo 14 del Decreto N.º 170 del " +
                "Ministerio de Transportes y Telecomunicaciones, \"Reglamento para el Otorgamiento de " +
                "Licencias de Conducir\", solicito a ustedes tengan a bien remitir, a través de la " +
                "Plataforma SGL, la carpeta con los antecedentes del siguiente conductor, para la " +
                "correspondiente emisión de su licencia de conducir:",
            "",
            $"Nombre: {request.FullName}",
            $"RUT: {request.Rut}",
            "",
            "Quedamos atentos a su respuesta.",
            "",
            "Saludos cordiales,",
            "Departamento de Licencias de Conducir",
            "Municipalidad de Valparaíso"
        };

        return string.Join("\n", lines);
    }
}
