using ClosedXML.Excel;
using LicenciasCarpetas.Domain;
using LicenciasCarpetas.Persistence;

namespace LicenciasCarpetas.Import;

public interface IExcelWorkbookImporter
{
    ImportSummary Import(string workbookPath);
    ImportSummary Import(IXLWorkbook workbook);
}

/// <summary>
/// Reads "DETALLE CARPETAS DEPTO. LICENCIAS DE CONDUCIR" into the database: the monthly agenda
/// sheets, the daily scanned/uploaded counters, and the comuna contact directory.
/// The workbook is only ever read; the database is the source of truth from the first import on.
/// </summary>
public sealed class ExcelWorkbookImporter(
    IFolderCaseRepository cases,
    IDailyCounterRepository counters,
    IComunaContactRepository contacts) : IExcelWorkbookImporter
{
    public ImportSummary Import(string workbookPath)
    {
        if (!File.Exists(workbookPath))
        {
            throw new FileNotFoundException($"No existe el archivo Excel: {workbookPath}", workbookPath);
        }

        try
        {
            // Opened through a shared read stream so the operator can keep the workbook open in
            // Excel / Google Drive while the import runs.
            using var stream = new FileStream(workbookPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var workbook = new XLWorkbook(stream);
            return Import(workbook);
        }
        catch (Exception ex) when (ex is not FileNotFoundException and not IOException)
        {
            // ClosedXML rejects the real workbook over its dropdown lists (data validations longer
            // than 255 characters), which the import does not need at all — retry without them.
            return ImportFromSanitizedCopy(workbookPath);
        }
    }

    private ImportSummary ImportFromSanitizedCopy(string workbookPath)
    {
        var sanitizedPath = WorkbookSanitizer.CreateCopyWithoutDataValidations(workbookPath);
        try
        {
            using var stream = new FileStream(sanitizedPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var workbook = new XLWorkbook(stream);
            return Import(workbook);
        }
        finally
        {
            File.Delete(sanitizedPath);
        }
    }

    /// <summary>
    /// Cada hoja se clasifica por sus encabezados, no por su nombre: el libro es de quien lo usa y
    /// las hojas se renombran ("ESCANEADAS Y SUBIDAS" pasó a "HOJA ESTADISTICAS"). Atado al nombre,
    /// el importador dejó de traer 149 días de contadores sin decir nada.
    /// </summary>
    public ImportSummary Import(IXLWorkbook workbook)
    {
        var summary = new ImportSummary();

        foreach (var sheet in workbook.Worksheets)
        {
            // El nombre sigue mandando para la agenda: distingue oficina y mes, que los encabezados
            // no dicen, y descarta las hojas PLANTILLA con la misma estructura pero sin datos.
            if (AgendaSheet.TryParse(sheet.Name) is { } agenda && HasHeader(sheet, "FECHA DE LA CITACION"))
            {
                ImportAgenda(sheet, agenda, summary);
                continue;
            }

            // Citas export from the booking system: a different tool than the master workbook, its
            // own headers ("FECHA CITA", not "FECHA DE LA CITACION"), one flat sheet instead of one
            // per office/month. Only name, RUT, date, office, email, phone and a best-effort licence
            // class come from here — everything else is filled in by hand later.
            if (HasHeader(sheet, "FECHA CITA") && HasHeader(sheet, "RUT") && HasHeader(sheet, "NOMBRE"))
            {
                ImportCitas(sheet, summary);
                continue;
            }

            if (HasHeader(sheet, "ESCANEADAS") && HasHeader(sheet, "SUBIDAS"))
            {
                ImportCounters(sheet, summary);
                continue;
            }

            if (HasHeader(sheet, "MUNICIPIO") && HasHeader(sheet, "CORREO"))
            {
                ImportDirectory(sheet, summary);
            }
        }

        return summary;
    }

    /// <summary>Busca un encabezado exacto en las primeras filas de la hoja.</summary>
    private static bool HasHeader(IXLWorksheet sheet, string header)
        => FindHeaderRow(sheet, header) is not null;

    private void ImportAgenda(IXLWorksheet sheet, AgendaSheet agenda, ImportSummary summary)
    {
        var headers = FindHeaderRow(sheet, "FECHA DE LA CITACION");
        if (headers is null)
        {
            summary.Warnings.Add($"Hoja '{sheet.Name}': no se encontró la fila de encabezados, se omitió completa.");
            return;
        }

        var (headerRow, columns) = headers.Value;
        var citationColumn = Column(columns, "FECHA DE LA CITACION");
        var uploadedColumn = Column(columns, "FECHA CUANDO SE SUBIO LA CARPETA");
        var lastFolderColumn = Column(columns, "FECHA ULTIMA CARPETA");
        var firstNameColumn = Column(columns, "NOMBRE");
        var lastNameColumn = Column(columns, "APELLIDO");
        var fullNameColumn = Column(columns, "NOMBRE COMPLETO");
        var rutColumn = Column(columns, "RUT");
        var attentionColumn = Column(columns, "ATENCION");
        var idoneityColumn = Column(columns, "IDEONIDAD MORAL") ?? Column(columns, "IDONEIDAD MORAL");
        var stateColumn = Column(columns, "ESTADO DE LA CARPETA");
        var decisionColumn = Column(columns, "DECISION FINAL");

        summary.SheetsRead++;

        // Las filas se juntan y se graban en una sola transacción por hoja: fila por fila, cada
        // escritura iba al disco por separado y el libro completo tardaba minuto y medio.
        var mappedRows = new List<Domain.FolderCase>();

        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? headerRow;
        for (var rowNumber = headerRow + 1; rowNumber <= lastRow; rowNumber++)
        {
            var row = sheet.Row(rowNumber);
            var raw = new RawAgendaRow(
                Read(row, citationColumn),
                Read(row, uploadedColumn),
                Read(row, lastFolderColumn),
                Read(row, firstNameColumn),
                Read(row, lastNameColumn),
                Read(row, fullNameColumn),
                Read(row, rutColumn),
                Read(row, attentionColumn),
                Read(row, idoneityColumn),
                Read(row, stateColumn),
                Read(row, decisionColumn));

            var mapped = AgendaRowMapper.Map(raw, agenda, rowNumber);
            if (mapped is null)
            {
                continue;
            }

            summary.RowsRead++;
            if (mapped.NeedsReview)
            {
                summary.CasesNeedingReview++;
            }

            mappedRows.Add(mapped);
        }

        // Toda la hoja se graba junta: una transacción por hoja en vez de una escritura por fila.
        foreach (var outcome in cases.UpsertMany(mappedRows))
        {
            if (outcome == UpsertOutcome.Inserted)
            {
                summary.CasesInserted++;
            }
            else
            {
                summary.CasesUpdated++;
            }
        }
    }

    private void ImportCitas(IXLWorksheet sheet, ImportSummary summary)
    {
        var headers = FindHeaderRow(sheet, "FECHA CITA");
        if (headers is null)
        {
            summary.Warnings.Add($"Hoja '{sheet.Name}': no se encontró la fila de encabezados de citas, se omitió completa.");
            return;
        }

        var (headerRow, columns) = headers.Value;
        var citationColumn = Column(columns, "FECHA CITA");
        var rutColumn = Column(columns, "RUT");
        var nameColumn = Column(columns, "NOMBRE");
        var emailColumn = Column(columns, "EMAIL");
        var phoneColumn = Column(columns, "TELEFONO");
        var tramiteColumn = Column(columns, "TRAMITE");
        var ubicacionColumn = Column(columns, "UBICACION");

        summary.SheetsRead++;

        var mappedRows = new List<Domain.FolderCase>();
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? headerRow;
        for (var rowNumber = headerRow + 1; rowNumber <= lastRow; rowNumber++)
        {
            var row = sheet.Row(rowNumber);
            var raw = new RawCitasRow(
                Read(row, citationColumn),
                Read(row, rutColumn),
                Read(row, nameColumn),
                Read(row, emailColumn),
                Read(row, phoneColumn),
                Read(row, tramiteColumn),
                Read(row, ubicacionColumn));

            var mapped = CitasRowMapper.Map(raw, rowNumber, sheet.Name);
            if (mapped is null)
            {
                continue;
            }

            summary.RowsRead++;
            if (mapped.NeedsReview)
            {
                summary.CasesNeedingReview++;
            }

            mappedRows.Add(mapped);
        }

        foreach (var outcome in cases.UpsertMany(mappedRows))
        {
            if (outcome == UpsertOutcome.Inserted)
            {
                summary.CasesInserted++;
            }
            else
            {
                summary.CasesUpdated++;
            }
        }
    }

    private void ImportCounters(IXLWorksheet sheet, ImportSummary summary)
    {
        var headers = FindHeaderRow(sheet, "ESCANEADAS");
        if (headers is null)
        {
            summary.Warnings.Add($"Hoja '{sheet.Name}': no se encontró la fila de encabezados de escaneadas/subidas.");
            return;
        }

        var (headerRow, columns) = headers.Value;
        summary.SheetsRead++;

        // The sheet repeats one month-block per set of columns: FECHA | ESCANEADAS | SUBIDAS | ...
        // Each block is read on its own, taking the columns between one FECHA header and the next.
        var dateColumns = columns
            .Where(entry => entry.Header == "FECHA")
            .Select(entry => entry.Column)
            .OrderBy(column => column)
            .ToList();

        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? headerRow;

        for (var blockIndex = 0; blockIndex < dateColumns.Count; blockIndex++)
        {
            var dateColumn = dateColumns[blockIndex];
            var blockEnd = blockIndex + 1 < dateColumns.Count ? dateColumns[blockIndex + 1] : int.MaxValue;

            var scannedColumn = ColumnInRange(columns, "ESCANEADAS", dateColumn, blockEnd);
            var uploadedColumn = ColumnInRange(columns, "SUBIDAS", dateColumn, blockEnd);
            if (scannedColumn is null && uploadedColumn is null)
            {
                continue;
            }

            for (var rowNumber = headerRow + 1; rowNumber <= lastRow; rowNumber++)
            {
                var row = sheet.Row(rowNumber);
                var date = CellValue.ToDate(Read(row, dateColumn));
                if (date is null)
                {
                    continue;
                }

                var scanned = ToCount(Read(row, scannedColumn));
                var uploaded = ToCount(Read(row, uploadedColumn));
                if (scanned is null && uploaded is null)
                {
                    continue;
                }

                counters.Upsert(new DailyCounter { Date = date.Value, Scanned = scanned, Uploaded = uploaded });
                summary.CountersImported++;
            }
        }
    }

    private void ImportDirectory(IXLWorksheet sheet, ImportSummary summary)
    {
        var headers = FindHeaderRow(sheet, "MUNICIPIO");
        if (headers is null)
        {
            summary.Warnings.Add($"Hoja '{sheet.Name}': no se encontró la fila de encabezados del directorio de comunas.");
            return;
        }

        var (headerRow, columns) = headers.Value;
        var comunaColumn = Column(columns, "MUNICIPIO");
        var emailColumn = Column(columns, "CORREO");
        summary.SheetsRead++;

        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? headerRow;
        for (var rowNumber = headerRow + 1; rowNumber <= lastRow; rowNumber++)
        {
            var row = sheet.Row(rowNumber);
            var comuna = CellValue.ToText(Read(row, comunaColumn));
            var email = CellValue.ToText(Read(row, emailColumn));

            if (comuna is null || email is null || !email.Contains('@'))
            {
                continue;
            }

            // Entries are written as "MUNICIP/ALTO HOSPICIO" — only the comuna itself is useful.
            var slash = comuna.LastIndexOf('/');
            if (slash >= 0 && slash < comuna.Length - 1)
            {
                comuna = comuna[(slash + 1)..].Trim();
            }

            contacts.Upsert(new ComunaContact { Comuna = comuna, Email = email });
            summary.ContactsImported++;
        }
    }

    /// <summary>Locates the header row by one header it must contain, and maps every header on that
    /// row to its column number — column order changes between sheets, header text does not.</summary>
    private static (int Row, List<(string Header, int Column)> Columns)? FindHeaderRow(IXLWorksheet sheet, string requiredHeader)
    {
        var target = TextNormalizer.Normalize(requiredHeader);
        var lastRow = Math.Min(sheet.LastRowUsed()?.RowNumber() ?? 1, 12);

        for (var rowNumber = 1; rowNumber <= lastRow; rowNumber++)
        {
            var row = sheet.Row(rowNumber);
            var lastColumn = row.LastCellUsed()?.Address.ColumnNumber ?? 0;
            var columns = new List<(string Header, int Column)>();
            var found = false;

            for (var column = 1; column <= lastColumn; column++)
            {
                var header = TextNormalizer.Normalize(row.Cell(column).GetString());
                if (header.Length == 0)
                {
                    continue;
                }

                columns.Add((header, column));
                found |= header == target;
            }

            if (found)
            {
                return (rowNumber, columns);
            }
        }

        return null;
    }

    private static int? Column(List<(string Header, int Column)> columns, string header)
    {
        var target = TextNormalizer.Normalize(header);
        foreach (var entry in columns)
        {
            if (entry.Header == target)
            {
                return entry.Column;
            }
        }
        return null;
    }

    private static int? ColumnInRange(List<(string Header, int Column)> columns, string header, int fromExclusive, int toExclusive)
    {
        var target = TextNormalizer.Normalize(header);
        foreach (var entry in columns)
        {
            if (entry.Header == target && entry.Column > fromExclusive && entry.Column < toExclusive)
            {
                return entry.Column;
            }
        }
        return null;
    }

    private static object? Read(IXLRow row, int? column)
    {
        if (column is null)
        {
            return null;
        }

        var cell = row.Cell(column.Value);
        var value = cell.Value;

        if (value.IsBlank)
        {
            return null;
        }

        if (value.IsDateTime)
        {
            return value.GetDateTime();
        }

        if (value.IsNumber)
        {
            // A number in a date-formatted cell is a serial date; CellValue.ToDate handles both.
            return value.GetNumber();
        }

        if (value.IsText)
        {
            return value.GetText();
        }

        return cell.GetFormattedString();
    }

    private static int? ToCount(object? value) => value switch
    {
        null => null,
        double number => (int)Math.Round(number),
        string text when int.TryParse(text, out var parsed) => parsed,
        _ => null
    };
}
