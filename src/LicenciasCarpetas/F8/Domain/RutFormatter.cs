namespace LicenciasCarpetas.F8.Domain;

/// <summary>Formats an already-canonical stored RUT ("12345678-9") with dots for display,
/// without re-parsing/re-validating — falls back to the raw string for anything that doesn't
/// look canonical (blank, legacy data) so display never throws on odd stored values.</summary>
public static class RutFormatter
{
    public static string? WithDots(string? canonicalRut) =>
        Rut.TryParse(canonicalRut, out var rut) ? rut.ToStringWithDots() : canonicalRut;
}
