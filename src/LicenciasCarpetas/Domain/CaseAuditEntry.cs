namespace LicenciasCarpetas.Domain;

public sealed class CaseAuditEntry
{
    public long Id { get; set; }
    public long FolderCaseId { get; set; }
    public string ChangedBy { get; set; } = string.Empty;
    public DateTimeOffset ChangedAt { get; set; } = DateTimeOffset.UtcNow;
    public string FieldName { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
}

