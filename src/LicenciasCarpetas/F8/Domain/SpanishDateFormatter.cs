namespace LicenciasCarpetas.F8.Domain;

/// <summary>
/// Long-form Spanish date for printed reports: day as digits, month spelled out, year as
/// digits — e.g. "15 de marzo de 2024" — matching the wording used on the office's F8 reports.
/// </summary>
public static class SpanishDateFormatter
{
    private static readonly string[] Meses =
    [
        "enero", "febrero", "marzo", "abril", "mayo", "junio",
        "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre",
    ];

    public static string LongDate(DateOnly? date) =>
        date is { } d ? $"{d.Day} de {Meses[d.Month - 1]} de {d.Year}" : "S/C";

    /// <summary>Parses "15 de mayo de 2024" (the format LongDate produces) back into a date.</summary>
    public static bool TryParseLongDate(string? text, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Trim().ToLowerInvariant().Split(" de ", StringSplitOptions.TrimEntries);
        if (parts.Length != 3 || !int.TryParse(parts[0], out var day) || !int.TryParse(parts[2], out var year))
        {
            return false;
        }

        var monthIndex = Array.IndexOf(Meses, parts[1]);
        if (monthIndex < 0)
        {
            return false;
        }

        try
        {
            date = new DateOnly(year, monthIndex + 1, day);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}
