using System.Text.RegularExpressions;
using LicenciasCarpetas.Domain;

namespace LicenciasCarpetas.Import;

/// <summary>
/// Turns a raw row from a citas export into a <see cref="FolderCase"/>. This is a different system
/// than the master workbook — it only ever brings name, RUT, citation date, office, email, phone and
/// (when it can be read from the "Tramite" free text) a licence class. Everything else on a case
/// stays for the operator to fill in by hand: this importer never touches folder state, decision or
/// moral idoneity.
/// </summary>
public static partial class CitasRowMapper
{
    /// <summary>Returns null when the row carries no person at all (spacer rows, trailing blanks).</summary>
    public static FolderCase? Map(RawCitasRow raw, int rowNumber, string sourceSheet)
    {
        var fullName = CellValue.ToText(raw.FullName);
        var rawRut = CellValue.ToText(raw.Rut);

        if (fullName is null && rawRut is null)
        {
            return null;
        }

        var validatedRut = RutValidator.NormalizeAndValidate(rawRut);
        var citationDate = CellValue.ToDate(raw.CitationDate);
        var office = OfficeCatalog.TryResolve(CellValue.ToText(raw.Ubicacion));
        var licenceClasses = LicenceClassCatalog.Serialize(ExtractLicenceClasses(CellValue.ToText(raw.Tramite)));

        var folderCase = new FolderCase
        {
            CitationDate = citationDate,
            FullName = TextNormalizer.DisplayUpper(fullName),
            Rut = validatedRut ?? rawRut,
            Office = office ?? Office.AvenidaArgentina,
            Email = CellValue.ToText(raw.Email),
            CellPhone = CellValue.ToText(raw.CellPhone),
            LicenceClasses = licenceClasses,
            SourceSheet = sourceSheet,
            SourceRow = rowNumber
        };

        folderCase.NeedsReview = fullName is null
            || validatedRut is null
            || citationDate is null
            || office is null;

        return folderCase;
    }

    /// <summary>
    /// Best-effort read of a licence class out of free text ("Primera vez, Extensión B"). Only exact
    /// catalog tokens count — anything else is left for the operator to add by hand later, as agreed:
    /// this is a courtesy extraction, not a guess.
    /// </summary>
    private static IReadOnlyList<LicenceClass> ExtractLicenceClasses(string? tramite)
    {
        if (tramite is null)
        {
            return [];
        }

        var found = new List<LicenceClass>();
        foreach (Match match in LicenceTokenPattern().Matches(tramite.ToUpperInvariant()))
        {
            if (Enum.TryParse<LicenceClass>(match.Value, out var licence) && !found.Contains(licence))
            {
                found.Add(licence);
            }
        }

        return found;
    }

    [GeneratedRegex(@"\b(A1|A2|A3|A4|A5|B|C|D|E|F)\b")]
    private static partial Regex LicenceTokenPattern();
}
