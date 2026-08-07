namespace LicenciasCarpetas.Domain;

/// <summary>
/// One entry of the "CORREOS CAMBIO DE DOMICLIO" sheet: which address to write to when a folder has
/// to be requested from another municipality. A comuna can have several contact addresses.
/// </summary>
public sealed class ComunaContact
{
    public long Id { get; set; }

    /// <summary>Comuna as written in the sheet, minus the "MUNICIP/" prefix ("ALTO HOSPICIO").</summary>
    public required string Comuna { get; set; }

    public required string Email { get; set; }

    public string? Notes { get; set; }
}
