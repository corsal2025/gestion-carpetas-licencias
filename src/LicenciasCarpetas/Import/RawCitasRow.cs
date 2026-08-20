namespace LicenciasCarpetas.Import;

/// <summary>
/// One row of a citas export ("citas_20260819_143008") exactly as it sits in the sheet, before any
/// interpretation. A different system than the master workbook, with its own column names.
/// </summary>
public sealed record RawCitasRow(
    object? CitationDate,
    object? Rut,
    object? FullName,
    object? Email,
    object? CellPhone,
    object? Tramite,
    object? Ubicacion);
