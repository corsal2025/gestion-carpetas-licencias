using ClosedXML.Excel;
using LicenciasCarpetas.Domain;
using LicenciasCarpetas.Import;
using LicenciasCarpetas.Persistence;

namespace LicenciasCarpetas.Tests;

public class ExcelWorkbookImporterTests
{
    /// <summary>Builds a miniature copy of the real workbook: one template sheet that must be
    /// ignored, two agenda sheets, the counters sheet and the comuna directory.</summary>
    private static XLWorkbook BuildWorkbook()
    {
        var workbook = new XLWorkbook();

        AddAgendaSheet(workbook, "PLANTILLA MODELO AV. ARGENTINA");
        var enero = AddAgendaSheet(workbook, "ENERO AV. ARGENTINA");
        WriteAgendaRow(enero, 3, new DateTime(2026, 1, 2), new DateTime(2026, 1, 20), new DateTime(2024, 9, 1),
            "Reinaldo", "Pizarro Aravena", "REINALDO PIZARRO ARAVENA", "13.025.150-1",
            "SI, EN AV. ARGENTINA", "", "SUBIDA A CONASET", "OTORGADO");
        WriteAgendaRow(enero, 4, new DateTime(2026, 1, 2), null, "LIMACHE",
            "Pamela", "Rojas Moyano", "PAMELA ROJAS MOYANO", "10.904.318-4",
            "SI, EN AV. ARGENTINA", "ALERTADA", "CAMBIO DOM. SUBIDO A CONASET", "");

        var placilla = AddAgendaSheet(workbook, "ENERO PLACILLA");
        WriteAgendaRow(placilla, 3, new DateTime(2026, 1, 2), null, null,
            "Margarita", "Parraguez", "MARGARITA PARRAGUEZ", "16.487.222-K",
            "", "", "1° LICENCIA", "");

        var counters = workbook.Worksheets.Add("ESCANEADAS Y SUBIDAS");
        counters.Cell(3, 3).Value = "FECHA";
        counters.Cell(3, 4).Value = "ESCANEADAS";
        counters.Cell(3, 5).Value = "SUBIDAS";
        counters.Cell(4, 3).Value = new DateTime(2026, 1, 2);
        counters.Cell(4, 4).Value = 44;
        counters.Cell(4, 5).Value = 37;

        var directory = workbook.Worksheets.Add("CORREOS CAMBIO DE DOMICLIO");
        directory.Cell(2, 1).Value = "Municipio";
        directory.Cell(2, 2).Value = "Correo";
        directory.Cell(3, 1).Value = "MUNICIP/ALTO HOSPICIO";
        directory.Cell(3, 2).Value = "nmolina@maho.cl";

        return workbook;
    }

    private static IXLWorksheet AddAgendaSheet(XLWorkbook workbook, string name)
    {
        var sheet = workbook.Worksheets.Add(name);
        sheet.Cell(1, 6).Value = "AGENDA MENSUAL";
        sheet.Cell(2, 1).Value = "FECHA DE LA CITACIÓN";
        sheet.Cell(2, 2).Value = "FECHA CUANDO SE SUBIO LA CARPETA ";
        sheet.Cell(2, 3).Value = "FECHA ULTIMA CARPETA";
        sheet.Cell(2, 4).Value = "NOMBRE ";
        sheet.Cell(2, 5).Value = "APELLIDO ";
        sheet.Cell(2, 6).Value = "NOMBRE COMPLETO ";
        sheet.Cell(2, 7).Value = "RUT";
        sheet.Cell(2, 8).Value = "ATENCIÓN ";
        sheet.Cell(2, 9).Value = "IDEONIDAD MORAL";
        sheet.Cell(2, 10).Value = "ESTADO DE LA CARPETA";
        sheet.Cell(2, 11).Value = "DECISIÓN FINAL";
        return sheet;
    }

    private static void WriteAgendaRow(IXLWorksheet sheet, int row, DateTime citation, DateTime? uploaded,
        object? lastFolder, string firstName, string lastName, string fullName, string rut,
        string attention, string idoneity, string state, string decision)
    {
        sheet.Cell(row, 1).Value = citation;
        if (uploaded is { } uploadedDate)
        {
            sheet.Cell(row, 2).Value = uploadedDate;
        }
        switch (lastFolder)
        {
            case DateTime date:
                sheet.Cell(row, 3).Value = date;
                break;
            case string text:
                sheet.Cell(row, 3).Value = text;
                break;
        }
        sheet.Cell(row, 4).Value = firstName;
        sheet.Cell(row, 5).Value = lastName;
        sheet.Cell(row, 6).Value = fullName;
        sheet.Cell(row, 7).Value = rut;
        sheet.Cell(row, 8).Value = attention;
        sheet.Cell(row, 9).Value = idoneity;
        sheet.Cell(row, 10).Value = state;
        sheet.Cell(row, 11).Value = decision;
    }

    private static (ExcelWorkbookImporter Importer, SqliteTestDatabase Db) Build()
    {
        var db = new SqliteTestDatabase();
        return (new ExcelWorkbookImporter(db.Cases, db.Counters, db.Contacts), db);
    }

    [Fact]
    public void Imports_agenda_counters_and_directory_sheets()
    {
        var (importer, db) = Build();
        using (db)
        using (var workbook = BuildWorkbook())
        {
            var summary = importer.Import(workbook);

            Assert.Equal(3, summary.CasesInserted);
            Assert.Equal(0, summary.CasesUpdated);
            Assert.Equal(1, summary.CountersImported);
            Assert.Equal(1, summary.ContactsImported);
            Assert.Empty(summary.Warnings);
        }
    }

    [Fact]
    public void Ignores_the_template_sheets()
    {
        var (importer, db) = Build();
        using (db)
        using (var workbook = BuildWorkbook())
        {
            importer.Import(workbook);

            var sheets = db.Cases.Query(new CaseFilter(), 0, 50).Select(c => c.SourceSheet).Distinct();
            Assert.DoesNotContain("PLANTILLA MODELO AV. ARGENTINA", sheets);
        }
    }

    [Fact]
    public void Reading_the_same_workbook_twice_does_not_duplicate_anybody()
    {
        var (importer, db) = Build();
        using (db)
        using (var workbook = BuildWorkbook())
        {
            importer.Import(workbook);
            var second = importer.Import(workbook);

            Assert.Equal(0, second.CasesInserted);
            Assert.Equal(3, second.CasesUpdated);
            Assert.Equal(3, db.Cases.Count(new CaseFilter()));
        }
    }

    [Fact]
    public void Keeps_the_comuna_written_in_the_last_folder_column()
    {
        var (importer, db) = Build();
        using (db)
        using (var workbook = BuildWorkbook())
        {
            importer.Import(workbook);

            var cambioDomicilio = db.Cases.Query(new CaseFilter { OnlyOtherComuna = true }, 0, 50);
            Assert.Single(cambioDomicilio);
            Assert.Equal("LIMACHE", cambioDomicilio[0].LastFolderComuna);
            Assert.Null(cambioDomicilio[0].LastFolderDate);
        }
    }

    [Fact]
    public void Assigns_each_row_to_the_office_of_its_sheet()
    {
        var (importer, db) = Build();
        using (db)
        using (var workbook = BuildWorkbook())
        {
            importer.Import(workbook);

            Assert.Equal(2, db.Cases.Count(new CaseFilter { Office = Office.AvenidaArgentina }));
            Assert.Equal(1, db.Cases.Count(new CaseFilter { Office = Office.Placilla }));
        }
    }

    [Fact]
    public void Imports_the_daily_scanned_and_uploaded_counters()
    {
        var (importer, db) = Build();
        using (db)
        using (var workbook = BuildWorkbook())
        {
            importer.Import(workbook);

            var counter = db.Counters.Find(new DateOnly(2026, 1, 2));
            Assert.NotNull(counter);
            Assert.Equal(44, counter.Scanned);
            Assert.Equal(37, counter.Uploaded);
        }
    }

    [Fact]
    public void Strips_the_municip_prefix_from_directory_entries()
    {
        var (importer, db) = Build();
        using (db)
        using (var workbook = BuildWorkbook())
        {
            importer.Import(workbook);

            var contact = Assert.Single(db.Contacts.All());
            Assert.Equal("ALTO HOSPICIO", contact.Comuna);
            Assert.Equal("nmolina@maho.cl", contact.Email);
        }
    }

    [Fact]
    public void A_missing_file_is_reported_as_such()
    {
        var (importer, db) = Build();
        using (db)
        {
            Assert.Throws<FileNotFoundException>(() => importer.Import(@"C:\no\existe\archivo.xlsx"));
        }
    }
}
