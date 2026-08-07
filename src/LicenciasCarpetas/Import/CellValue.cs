using System.Globalization;
using LicenciasCarpetas.Domain;

namespace LicenciasCarpetas.Import;

/// <summary>Type-tolerant readers for workbook cells filled in by hand over a whole year.</summary>
public static class CellValue
{
    public static string? ToText(object? value)
    {
        var text = value switch
        {
            null => null,
            string s => s,
            DateTime date => date.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture),
            double number => number.ToString(CultureInfo.InvariantCulture),
            _ => value.ToString()
        };

        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    /// <summary>
    /// Reads a date cell whether Excel stored it as a real date, as a serial number, or as text the
    /// operator typed ("15/03/2024", "15 marzo 2024"). Returns null for anything else — a comuna
    /// name in the última-carpeta column must not silently become a date.
    /// </summary>
    public static DateOnly? ToDate(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case DateTime date:
                return DateOnly.FromDateTime(date);
            case double serial:
                return serial is > 0 and < 2958466 // Excel's own 1900-01-01..9999-12-31 serial range
                    ? DateOnly.FromDateTime(DateTime.FromOADate(serial))
                    : null;
            case string text when SpanishDate.TryParse(text, out var parsed):
                return parsed;
            case string text when DateTime.TryParse(text, CultureInfo.GetCultureInfo("es-CL"),
                    DateTimeStyles.None, out var parsedDate):
                return DateOnly.FromDateTime(parsedDate);
            default:
                return null;
        }
    }
}
