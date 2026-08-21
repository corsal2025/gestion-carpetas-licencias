using System.Globalization;
using System.Text;

namespace LicenciasCarpetas.F8.Import;

/// <summary>
/// Maps a worksheet's header row (as raw cell text) to canonical field names,
/// resolving columns by NAME rather than position — required because MAYO uses
/// a 10-column reordered layout (no FECHA DE SUBIDA) while JUNIO/JULIO use the
/// standard 11-column layout. Pure function, no ClosedXML dependency.
/// </summary>
public static class HeaderCanonicalizer
{
    public const string FechaPeticion = "FECHA PETICION";
    public const string Nombres = "NOMBRES";
    public const string Apellidos = "APELLIDOS";
    public const string NombreCompleto = "NOMBRE COMPLETO";
    public const string Rut = "RUT";
    public const string FechaUltimaCarpeta = "FECHA ULTIMA CARPETA";
    public const string CodigoF8 = "CODIGO F8";
    public const string FechaPenultimaCarpeta = "FECHA PENULTIMA CARPETA";
    public const string Estado = "ESTADO";
    public const string EstadoActual = "ESTADO ACTUAL";
    public const string FechaDeSubida = "FECHA DE SUBIDA";

    private static readonly Dictionary<string, string> Synonyms = new(StringComparer.Ordinal)
    {
        ["FECHA PETICION"] = FechaPeticion,
        ["FECHA DE PETICION"] = FechaPeticion,
        ["NOMBRES"] = Nombres,
        ["APELLIDOS"] = Apellidos,
        ["NOMBRE COMPLETO"] = NombreCompleto,
        ["RUT"] = Rut,
        ["FECHA ULTIMA CARPETA"] = FechaUltimaCarpeta,
        ["FECHA DE ULTIMA CARPETA"] = FechaUltimaCarpeta,
        ["CODIGO F8"] = CodigoF8,
        ["CODIGO DE F8"] = CodigoF8,
        ["FECHA PENULTIMA CARPETA"] = FechaPenultimaCarpeta,
        ["FECHA DE PENULTIMA CARPETA"] = FechaPenultimaCarpeta,
        ["ESTADO"] = Estado,
        ["ESTADO ACTUAL"] = EstadoActual,
        ["FECHA DE SUBIDA"] = FechaDeSubida,
        ["FECHA SUBIDA"] = FechaDeSubida,
    };

    public static IReadOnlyDictionary<string, int> Canonicalize(IReadOnlyList<string?> headerCells)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < headerCells.Count; i++)
        {
            var normalized = Normalize(headerCells[i]);
            if (normalized.Length == 0)
            {
                continue;
            }

            if (Synonyms.TryGetValue(normalized, out var canonical) && !map.ContainsKey(canonical))
            {
                map[canonical] = i;
            }
        }

        return map;
    }

    private static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var stripped = StripAccents(raw.Trim().ToUpperInvariant());
        var collapsed = string.Join(' ', stripped.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return collapsed;
    }

    private static string StripAccents(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();
        foreach (var c in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
