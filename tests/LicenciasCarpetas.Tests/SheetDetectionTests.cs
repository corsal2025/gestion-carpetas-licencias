using ClosedXML.Excel;
using LicenciasCarpetas.Import;
using LicenciasCarpetas.Persistence;

namespace LicenciasCarpetas.Tests;

/// <summary>
/// Las hojas se reconocen por lo que contienen, no por cómo se llaman. El operador renombró
/// "ESCANEADAS Y SUBIDAS" a "HOJA ESTADISTICAS" y la importación dejó de traer los contadores en
/// silencio: 149 días de escaneadas y subidas se perdían sin un solo aviso.
/// </summary>
public class SheetDetectionTests
{
    private static XLWorkbook WorkbookWithCountersSheetNamed(string sheetName)
    {
        var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(sheetName);
        sheet.Cell(2, 2).Value = "FECHA";
        sheet.Cell(2, 3).Value = "ESCANEADAS";
        sheet.Cell(2, 4).Value = "SUBIDAS";
        sheet.Cell(3, 2).Value = new DateTime(2026, 1, 2);
        sheet.Cell(3, 3).Value = 44;
        sheet.Cell(3, 4).Value = 37;
        return workbook;
    }

    private static (ExcelWorkbookImporter Importer, SqliteTestDatabase Db) Build()
    {
        var db = new SqliteTestDatabase();
        return (new ExcelWorkbookImporter(db.Cases, db.Counters, db.Contacts), db);
    }

    [Theory]
    [InlineData("ESCANEADAS Y SUBIDAS")]
    [InlineData("HOJA ESTADISTICAS")]
    [InlineData("estadisticas 2027")]
    public void The_counters_sheet_is_found_whatever_it_is_called(string sheetName)
    {
        var (importer, db) = Build();
        using (db)
        using (var workbook = WorkbookWithCountersSheetNamed(sheetName))
        {
            var summary = importer.Import(workbook);

            Assert.Equal(1, summary.CountersImported);
            var counter = db.Counters.Find(new DateOnly(2026, 1, 2));
            Assert.Equal(44, counter!.Scanned);
            Assert.Equal(37, counter.Uploaded);
        }
    }

    /// <summary>Una hoja de agenda no debe confundirse con la de contadores ni al revés.</summary>
    [Fact]
    public void An_agenda_sheet_is_not_mistaken_for_the_counters_sheet()
    {
        var (importer, db) = Build();
        using (db)
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.Worksheets.Add("ENERO AV. ARGENTINA");
            sheet.Cell(2, 1).Value = "FECHA DE LA CITACIÓN";
            sheet.Cell(2, 6).Value = "NOMBRE COMPLETO";
            sheet.Cell(2, 7).Value = "RUT";
            sheet.Cell(3, 1).Value = new DateTime(2026, 1, 2);
            sheet.Cell(3, 6).Value = "JUAN PEREZ";
            sheet.Cell(3, 7).Value = "13.025.150-1";

            var summary = importer.Import(workbook);

            Assert.Equal(1, summary.CasesInserted);
            Assert.Equal(0, summary.CountersImported);
        }
    }

    [Fact]
    public void The_comuna_directory_is_also_found_by_its_columns()
    {
        var (importer, db) = Build();
        using (db)
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.Worksheets.Add("CORREOS 2027");
            sheet.Cell(1, 1).Value = "Municipio";
            sheet.Cell(1, 2).Value = "Correo";
            sheet.Cell(2, 1).Value = "MUNICIP/QUILPUÉ";
            sheet.Cell(2, 2).Value = "transito@muniquilpue.cl";

            var summary = importer.Import(workbook);

            Assert.Equal(1, summary.ContactsImported);
            Assert.Equal("QUILPUÉ", Assert.Single(db.Contacts.All()).Comuna);
        }
    }

    /// <summary>Una hoja que no es ninguna de las tres se ignora sin ruido.</summary>
    [Fact]
    public void An_unrelated_sheet_is_ignored()
    {
        var (importer, db) = Build();
        using (db)
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.Worksheets.Add("NOTAS");
            sheet.Cell(1, 1).Value = "recordar pedir toner";

            var summary = importer.Import(workbook);

            Assert.Equal(0, summary.SheetsRead);
            Assert.Empty(summary.Warnings);
        }
    }
}
