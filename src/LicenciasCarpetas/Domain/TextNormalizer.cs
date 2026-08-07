using System.Globalization;
using System.Text;

namespace LicenciasCarpetas.Domain;

/// <summary>
/// The workbook was filled in by hand for a whole year, so the same value appears written several
/// ways ("ESPERA EXÁMEN" / "ESPERA EXAMEN", "SE ENCUENTRA EN OF.43" / "SE ENCUENTRA EN OF. 43").
/// Every catalog lookup goes through this so those variants collapse into one canonical value.
/// </summary>
public static class TextNormalizer
{
    /// <summary>Upper-cases, strips accents, and collapses whitespace and punctuation noise.</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var decomposed = text.Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var lastWasSpace = false;

        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                    lastWasSpace = true;
                }
                continue;
            }

            builder.Append(c);
            lastWasSpace = false;
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>Normalize plus removal of every non-alphanumeric character — the loosest form,
    /// used for catalog matching where only the letters carry meaning.</summary>
    public static string NormalizeLoose(string? text)
    {
        var normalized = Normalize(text);
        var builder = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
            }
        }
        return builder.ToString();
    }
}
