namespace LicenciasCarpetas.CambioDomicilio.Domain;

public enum OutboundRequestStatus
{
    Borrador,
    Enviada
}

/// <summary>A request Valparaíso sends to another comuna asking it to confirm/return a
/// contributor's folder after an address change — the mirror direction of PersonRequest, which
/// tracks requests other comunas send TO Valparaíso.</summary>
public sealed class OutboundAddressChangeRequest
{
    public long Id { get; set; }
    public required string FullName { get; set; }
    public required string Rut { get; set; }
    public string? Phone { get; set; }
    public required string Street { get; set; }
    public required string Number { get; set; }
    public string? Unit { get; set; }
    public required string DestinationComuna { get; set; }
    public OutboundRequestStatus Status { get; set; } = OutboundRequestStatus.Borrador;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SentAt { get; set; }
    public long? SentByUserId { get; set; }
    public long CreatedByUserId { get; set; }
}

/// <summary>One file attached to an OutboundAddressChangeRequest — stored on disk under
/// CarpetasOptions.UploadDirectory, only the metadata lives in SQLite.</summary>
public sealed class OutboundAddressChangeAttachment
{
    public long Id { get; set; }
    public long RequestId { get; set; }
    public required string FileName { get; set; }
    public required string StoredPath { get; set; }
    public required string ContentType { get; set; }
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;
}
