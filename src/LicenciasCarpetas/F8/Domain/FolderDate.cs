using System.Globalization;

namespace LicenciasCarpetas.F8.Domain;

public enum FolderDateOutcome
{
    Ok,
    SinCarpeta,
    OutOfRange,
    Unparseable,
}

public readonly record struct FolderDateResult(DateOnly? Value, FolderDateOutcome Outcome);

/// <summary>
/// Parses the folder-date columns from the historical Excel workbook.
/// "S/C" and blank both mean "sin carpeta" (typed null, not an error).
/// Excel serial numbers are converted to DateOnly; out-of-range serials
/// (e.g. the real MAYO H54 value 29262516) and unparseable text are
/// surfaced as distinct outcomes so the importer can flag-not-drop.
/// </summary>
public static class FolderDate
{
    private static readonly DateOnly ExcelEpoch = new(1899, 12, 30);
    private static readonly DateOnly MinReasonable = new(1900, 1, 1);
    private static readonly DateOnly MaxReasonable = new(2100, 1, 1);

    public static FolderDateResult Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Trim().Equals("S/C", StringComparison.OrdinalIgnoreCase))
        {
            return new FolderDateResult(null, FolderDateOutcome.SinCarpeta);
        }

        var text = raw.Trim();

        if (DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var isoDate))
        {
            return new FolderDateResult(isoDate, FolderDateOutcome.Ok);
        }

        if (double.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var serial))
        {
            if (serial is > int.MaxValue or < int.MinValue)
            {
                // Guards the (int) cast below: an out-of-int-range serial (the real MAYO H54
                // corrupt cell can surface here as a huge double) would otherwise silently
                // wrap instead of throwing, which AddDays relies on to signal OutOfRange.
                return new FolderDateResult(null, FolderDateOutcome.OutOfRange);
            }

            try
            {
                var candidate = ExcelEpoch.AddDays((int)serial);
                if (candidate < MinReasonable || candidate > MaxReasonable)
                {
                    return new FolderDateResult(null, FolderDateOutcome.OutOfRange);
                }

                return new FolderDateResult(candidate, FolderDateOutcome.Ok);
            }
            catch (ArgumentOutOfRangeException)
            {
                return new FolderDateResult(null, FolderDateOutcome.OutOfRange);
            }
        }

        return new FolderDateResult(null, FolderDateOutcome.Unparseable);
    }
}
