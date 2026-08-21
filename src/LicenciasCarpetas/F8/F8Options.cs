namespace LicenciasCarpetas.F8;

// La base y el login ya no son propios de F8 — vive en la misma carpetas.db y detrás del mismo
// login que el resto de la app (ver claim mod:f8-urgentes). Solo quedan las rutas de Excel.
public sealed class F8Options
{
    public const string SectionName = "F8";

    public string? ExcelSourcePath { get; set; }
    public string? MatrizExcelPath { get; set; }
}
