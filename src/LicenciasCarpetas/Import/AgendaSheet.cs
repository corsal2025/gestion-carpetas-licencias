using LicenciasCarpetas.Domain;

namespace LicenciasCarpetas.Import;

/// <summary>An agenda sheet title decomposed into the office it belongs to and the period it covers.</summary>
public sealed record AgendaSheet(string Title, Office Office, int? Month)
{
    /// <summary>"MAYO PLACILLA" / "2DO SEM. MERC. PUERTO" — but not the PLANTILLA MODELO templates,
    /// which carry the same column layout with only dropdown sample values in them.</summary>
    public static AgendaSheet? TryParse(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var normalized = TextNormalizer.Normalize(title);
        if (normalized.StartsWith("PLANTILLA", StringComparison.Ordinal))
        {
            return null;
        }

        var office = OfficeCatalog.TryResolve(normalized);
        if (office is null)
        {
            return null;
        }

        return new AgendaSheet(title.Trim(), office.Value, TryParseMonth(normalized));
    }

    private static int? TryParseMonth(string normalizedTitle)
    {
        for (var month = 1; month <= 12; month++)
        {
            var name = TextNormalizer.Normalize(SpanishDate.MonthName(month));
            if (normalizedTitle.StartsWith(name, StringComparison.Ordinal))
            {
                return month;
            }
        }

        // "2DO SEM. ..." sheets hold a whole half-year; the citation date of each row carries the month.
        return null;
    }
}
