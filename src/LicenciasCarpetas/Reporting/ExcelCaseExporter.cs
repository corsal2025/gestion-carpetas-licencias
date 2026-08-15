using ClosedXML.Excel;
using LicenciasCarpetas.Domain;

namespace LicenciasCarpetas.Reporting;

public interface IExcelCaseExporter
{
    /// <summary>Writes the given cases into a workbook with the same column layout the operator knows
    /// from "DETALLE CARPETAS", and returns the file bytes.</summary>
    byte[] Export(IReadOnlyList<FolderCase> cases, string sheetTitle);
}

public sealed class ExcelCaseExporter : IExcelCaseExporter
{
    private static readonly string[] Headers =
    [
        "FECHA DE LA CITACIÓN",
        "FECHA CUANDO SE SUBIO LA CARPETA",
        "FECHA ULTIMA CARPETA",
        "NOMBRE COMPLETO",
        "RUT",
        "ATENCIÓN",
        "IDONEIDAD MORAL",
        "ESTADO DE LA CARPETA",
        "DECISIÓN FINAL",
        "SECTOR",
        "REQUIERE REVISIÓN"
    ];

    public byte[] Export(IReadOnlyList<FolderCase> cases, string sheetTitle)
    {
        using var workbook = new XLWorkbook();
        // Excel rejects sheet names over 31 chars or carrying \/?*[]: — the title comes from filter
        // text, so it gets trimmed here rather than blowing up on save.
        var worksheet = workbook.Worksheets.Add(SafeSheetName(sheetTitle));

        for (var column = 0; column < Headers.Length; column++)
        {
            worksheet.Cell(1, column + 1).Value = Headers[column];
        }
        worksheet.Row(1).Style.Font.Bold = true;

        var row = 2;
        foreach (var item in cases)
        {
            SetDate(worksheet.Cell(row, 1), item.CitationDate);
            SetDate(worksheet.Cell(row, 2), item.FolderUploadedDate);

            if (item.LastFolderDate is { } lastFolder)
            {
                SetDate(worksheet.Cell(row, 3), lastFolder);
            }
            else
            {
                worksheet.Cell(row, 3).Value = item.LastFolderComuna ?? string.Empty;
            }

            worksheet.Cell(row, 4).Value = item.FullName ?? string.Empty;
            worksheet.Cell(row, 5).Value = item.Rut ?? string.Empty;
            worksheet.Cell(row, 6).Value = item.AttentionNote ?? string.Empty;
            worksheet.Cell(row, 7).Value = item.MoralIdoneity is { } idoneity ? MoralIdoneityCatalog.Display(idoneity) : string.Empty;
            worksheet.Cell(row, 8).Value = item.FolderStateText;
            worksheet.Cell(row, 9).Value = item.FinalDecisionText;
            worksheet.Cell(row, 10).Value = item.Sector is null ? string.Empty : FolderSectorCatalog.Display(item.Sector);
            worksheet.Cell(row, 11).Value = item.NeedsReview ? "SÍ" : string.Empty;
            row++;
        }

        worksheet.Columns().AdjustToContents();
        worksheet.SheetView.FreezeRows(1);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void SetDate(IXLCell cell, DateOnly? date)
    {
        if (date is null)
        {
            return;
        }

        cell.Value = date.Value.ToDateTime(TimeOnly.MinValue);
        cell.Style.DateFormat.Format = "dd-mm-yyyy";
    }

    private static string SafeSheetName(string title)
    {
        var cleaned = new string(title.Where(c => !"\\/?*[]:".Contains(c)).ToArray()).Trim();
        if (cleaned.Length == 0)
        {
            cleaned = "Casos";
        }
        return cleaned.Length <= 31 ? cleaned : cleaned[..31];
    }
}
