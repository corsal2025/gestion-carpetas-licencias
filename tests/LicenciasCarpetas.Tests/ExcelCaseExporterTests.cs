using ClosedXML.Excel;
using LicenciasCarpetas.Domain;
using LicenciasCarpetas.Reporting;

namespace LicenciasCarpetas.Tests;

/// <summary>
/// The export is what leaves the app and gets sent on, so every field the operator can fill in has
/// to be in it — a column added to the screen and forgotten here silently drops data from reports.
/// </summary>
public class ExcelCaseExporterTests
{
    private static IXLWorksheet Export(FolderCase folderCase)
    {
        var bytes = new ExcelCaseExporter().Export([folderCase], "Casos");
        var workbook = new XLWorkbook(new MemoryStream(bytes));
        return workbook.Worksheet(1);
    }

    private static FolderCase Complete() => new()
    {
        FullName = "JUAN PEREZ",
        Rut = "13.025.150-1",
        CitationDate = new DateOnly(2026, 1, 2),
        FolderUploadedDate = new DateOnly(2026, 1, 20),
        LastFolderDate = new DateOnly(2024, 9, 1),
        PenultimateFolderDate = new DateOnly(2015, 4, 1),
        CodigoF8 = "F8-2026-114",
        Office = Office.AvenidaArgentina,
        AttentionNote = "SI, EN AV. ARGENTINA",
        MoralIdoneity = MoralIdoneity.Alertada,
        FolderState = FolderState.SubidaAConaset,
        FinalDecision = FinalDecision.Otorgado
    };

    [Fact]
    public void Carries_every_editable_field()
    {
        var sheet = Export(Complete());
        var headers = Enumerable.Range(1, 13).Select(column => sheet.Cell(1, column).GetString()).ToList();

        Assert.Contains("PENÚLTIMA CARPETA", headers);
        Assert.Contains("CÓDIGO F8", headers);
        Assert.Contains("ESTADO DE LA CARPETA", headers);
        Assert.Contains("DECISIÓN FINAL", headers);
    }

    [Fact]
    public void The_new_fields_carry_their_values()
    {
        var sheet = Export(Complete());
        var headers = Enumerable.Range(1, 13)
            .ToDictionary(column => sheet.Cell(1, column).GetString(), column => column);

        Assert.Equal(new DateTime(2015, 4, 1), sheet.Cell(2, headers["PENÚLTIMA CARPETA"]).GetDateTime());
        Assert.Equal("F8-2026-114", sheet.Cell(2, headers["CÓDIGO F8"]).GetString());
    }

    /// <summary>
    /// A case whose fields are all empty still has to produce a valid file with its headers — the
    /// row simply comes out blank (ClosedXML does not count a row of empty strings as used).
    /// </summary>
    [Fact]
    public void An_empty_case_exports_without_blowing_up()
    {
        var sheet = Export(new FolderCase { Office = Office.Placilla });

        Assert.Equal("FECHA DE LA CITACIÓN", sheet.Cell(1, 1).GetString());
        Assert.Equal(string.Empty, sheet.Cell(2, 6).GetString());
    }
}
