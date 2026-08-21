namespace LicenciasCarpetas.CambioDomicilio.Domain;

/// <summary>CSV-sourced routing entry mapping a comuna's email domain to its contact address.
/// Named ComunaRoutingEntry (not ComunaContact) to avoid colliding with
/// LicenciasCarpetas.Domain.ComunaContact, which is a different notification-table type.</summary>
public sealed record ComunaRoutingEntry(string Comuna, string ContactEmail, string Domain);

public sealed record IncomingEmail(
    string MessageId,
    string ConversationId,
    string Subject,
    string SenderAddress,
    string BodyText,
    DateTimeOffset ReceivedAt);
