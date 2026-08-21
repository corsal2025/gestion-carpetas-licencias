namespace LicenciasCarpetas.F8.Import;

public sealed record SheetImportSummary(string SheetName, int RowsRead, int RowsImported, int RowsFlagged, int RowsSkipped);

/// <summary>Per-sheet + total counts for one importer run, printed to console by the CLI handler.</summary>
public sealed class ImportResult
{
    public List<SheetImportSummary> Sheets { get; } = new();

    public int TotalRowsRead => Sheets.Sum(s => s.RowsRead);
    public int TotalRowsImported => Sheets.Sum(s => s.RowsImported);
    public int TotalRowsFlagged => Sheets.Sum(s => s.RowsFlagged);
    public int TotalRowsSkipped => Sheets.Sum(s => s.RowsSkipped);

    public bool NoOp { get; init; }

    public override string ToString()
    {
        if (NoOp)
        {
            return "Import skipped: this source file was already imported. Use --force to re-import.";
        }

        var lines = Sheets.Select(s =>
            $"  {s.SheetName}: read={s.RowsRead} imported={s.RowsImported} flagged={s.RowsFlagged} skipped={s.RowsSkipped}");
        return "Import summary:" + Environment.NewLine + string.Join(Environment.NewLine, lines) +
            Environment.NewLine + $"  TOTAL: read={TotalRowsRead} imported={TotalRowsImported} flagged={TotalRowsFlagged} skipped={TotalRowsSkipped}";
    }
}
