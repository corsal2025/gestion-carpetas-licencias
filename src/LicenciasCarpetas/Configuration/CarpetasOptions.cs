namespace LicenciasCarpetas.Configuration;

public sealed class CarpetasOptions
{
    public const string SectionName = "Carpetas";

    /// <summary>SQLite file that becomes the source of truth once the workbook has been imported.</summary>
    public string SqliteDbPath { get; set; } = "data/carpetas.db";

    /// <summary>Default workbook offered on the import screen — the operator can point at any other file.</summary>
    public string DefaultWorkbookPath { get; set; } = string.Empty;

    /// <summary>Folder where generated .xlsx exports are written.</summary>
    public string ExportDirectory { get; set; } = "data/exports";

    /// <summary>Rows per page on the cases screen — the workbook holds ~21.000 rows, so paging is not optional.</summary>
    public int PageSize { get; set; } = 100;
}
