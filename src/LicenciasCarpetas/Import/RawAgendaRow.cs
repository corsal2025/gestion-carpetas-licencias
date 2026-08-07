namespace LicenciasCarpetas.Import;

/// <summary>
/// One agenda row exactly as it sits in the workbook, before any interpretation. Values arrive as
/// <see cref="object"/> because the same column mixes types: "FECHA ULTIMA CARPETA" holds a real
/// date for most rows and a comuna name for cambio-de-domicilio rows.
/// </summary>
public sealed record RawAgendaRow(
    object? CitationDate,
    object? FolderUploadedDate,
    object? LastFolder,
    object? FirstName,
    object? LastName,
    object? FullName,
    object? Rut,
    object? Attention,
    object? MoralIdoneity,
    object? FolderState,
    object? FinalDecision);
