namespace LicenciasCarpetas.Import;

/// <summary>What a workbook import did, reported back to the operator on the /Importar screen.</summary>
public sealed class ImportSummary
{
    public int SheetsRead { get; set; }
    public int RowsRead { get; set; }
    public int CasesInserted { get; set; }
    public int CasesUpdated { get; set; }
    public int CasesNeedingReview { get; set; }
    public int CountersImported { get; set; }
    public int ContactsImported { get; set; }

    /// <summary>Sheets or rows that could not be read at all, with the reason. Never thrown away silently.</summary>
    public List<string> Warnings { get; } = [];

    public void Merge(ImportSummary other)
    {
        SheetsRead += other.SheetsRead;
        RowsRead += other.RowsRead;
        CasesInserted += other.CasesInserted;
        CasesUpdated += other.CasesUpdated;
        CasesNeedingReview += other.CasesNeedingReview;
        CountersImported += other.CountersImported;
        ContactsImported += other.ContactsImported;
        Warnings.AddRange(other.Warnings);
    }
}
