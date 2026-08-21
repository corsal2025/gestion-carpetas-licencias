using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using LicenciasCarpetas.F8.Data;
using LicenciasCarpetas.F8.Domain;

namespace LicenciasCarpetas.F8.Matriz;

/// <summary>
/// Reads the "matriz de distribucion de carpetas" workbook (a separate spreadsheet from
/// URGENTES DIARIOS.xlsx, maintained by the office to track physical folders) to auto-create
/// cases flagged "NO HAY CARPETA" (entrada), and writes back "SUBIDA CON F8" + today's date
/// once the operator marks a case uploaded in the dashboard (salida).
///
/// Uses the raw DocumentFormat.OpenXml SDK instead of ClosedXML for this specific file: the
/// real matriz workbook has data-validation dropdown lists (ESTADO/DECISION columns) whose
/// formula text exceeds ClosedXML's hardcoded 255-char limit, which throws
/// ArgumentOutOfRangeException the instant ClosedXML tries to *load* the file — before any
/// cell is even read. The OpenXml SDK never parses data validations unless explicitly asked
/// to, so it opens/edits this file without touching (or needing to understand) them at all.
/// Writes only ever touch the two target cells' own XML nodes — nothing else in the workbook
/// (styles, formulas, validations, other sheets) is rewritten — and a timestamped backup copy
/// is taken before every save.
/// </summary>
public static class MatrizSyncService
{
    private static readonly string[] KnownSectors = ["AV. ARGENTINA", "PLACILLA", "MERC. PUERTO"];
    // The real workbook uses "NO EXISTE CARPETA" — "NO HAY CARPETA" is kept as a second accepted
    // spelling since operators across sheets/months aren't perfectly consistent in wording.
    private static readonly string[] EstadosSinCarpeta = ["NO EXISTE CARPETA", "NO HAY CARPETA"];
    private const string EstadoSubidaConF8 = "SUBIDA CON F8";
    private const int MaxRetries = 4;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    // Salida (write-back to the Excel matriz) is disabled by request — the office wants to do
    // that step by hand for now, until the matching/row-relocation logic is more battle-tested.
    // Entrada (reading NO EXISTE CARPETA cases into the dashboard) keeps running as normal.
    // Cases marked "Subir" still queue up (PendienteEscrituraExcel stays true) so nothing is
    // lost — flip this back to true whenever salida should resume.
    private static readonly bool SalidaEnabled = false;

    public static MatrizSyncResult Sync(string? workbookPath, IUrgentRequestRepository repository)
    {
        var result = new MatrizSyncResult();
        if (string.IsNullOrWhiteSpace(workbookPath) || !File.Exists(workbookPath))
        {
            result.NoOp = true;
            return result;
        }

        RunEntrada(workbookPath, repository, result);
        if (SalidaEnabled)
        {
            RunSalida(workbookPath, repository, result);
        }
        return result;
    }

    // Entrada: opened read-only (isEditable: false) — the OpenXml SDK cannot alter the file
    // under a read-only open, so this pass is inherently non-destructive.
    private static void RunEntrada(string workbookPath, IUrgentRequestRepository repository, MatrizSyncResult result)
    {
        using var document = SpreadsheetDocument.Open(workbookPath, isEditable: false);
        var workbookPart = document.WorkbookPart!;
        var sharedStrings = workbookPart.GetPartsOfType<SharedStringTablePart>().FirstOrDefault();

        foreach (var sheet in workbookPart.Workbook.Sheets!.Elements<Sheet>())
        {
            var sheetName = sheet.Name!.Value!;
            var sector = SectorFor(sheetName);
            if (sector is null)
            {
                continue;
            }

            var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id!.Value!);
            var sheetData = worksheetPart.Worksheet.GetFirstChild<SheetData>();
            if (sheetData is null)
            {
                continue;
            }

            var columns = MapColumns(sheetData, sharedStrings);
            if (columns is null)
            {
                continue;
            }

            foreach (var row in sheetData.Elements<Row>())
            {
                var rowNumber = (int)row.RowIndex!.Value;
                if (rowNumber < 3)
                {
                    continue;
                }

                var rut = GetCellText(row, columns.Rut, sharedStrings)?.Trim() ?? "";
                var nombre = GetCellText(row, columns.NombreCompleto, sharedStrings)?.Trim() ?? "";
                var estado = GetCellText(row, columns.EstadoCarpeta, sharedStrings)?.Trim() ?? "";

                if (rut.Length == 0 && nombre.Length == 0 && estado.Length == 0)
                {
                    // First fully-blank row ends the real data block.
                    break;
                }

                if (!EstadosSinCarpeta.Contains(NormalizeHeader(estado), StringComparer.Ordinal))
                {
                    continue;
                }

                if (!Rut.TryParse(rut, out var parsedRut))
                {
                    result.Alerts.Add($"RUT invalido en hoja '{sheetName}' fila {rowNumber}: '{rut}' — revisar manualmente.");
                    continue;
                }

                var normalizedRut = parsedRut.ToString();
                if (repository.FindByRut(normalizedRut) is not null)
                {
                    continue;
                }

                var fechaCitacionRaw = columns.FechaCitacion is { } fechaCol ? GetCellText(row, fechaCol, sharedStrings) : null;
                var fechaCitacion = FolderDate.Parse(fechaCitacionRaw).Value;

                repository.Insert(new UrgentRequest
                {
                    NombreCompleto = nombre.Length > 0 ? nombre : null,
                    Rut = normalizedRut,
                    RutRaw = rut,
                    FechaPeticion = fechaCitacion,
                    MatrizSector = sector,
                    SourceSheet = sheetName,
                    SourceRowNumber = rowNumber,
                    Origin = "Matriz",
                    NeedsReview = !parsedRut.IsValid,
                    CreatedAt = DateTimeOffset.UtcNow,
                });

                result.CasesCreated++;
            }
        }
    }

    // Salida: only opens the workbook for writing (isEditable: true) when there is at least one
    // pending row. Retries on lock instead of giving up — the office keeps this file open in
    // Excel/Drive often — and takes a backup copy before saving.
    private static void RunSalida(string workbookPath, IUrgentRequestRepository repository, MatrizSyncResult result)
    {
        var allPending = repository.GetPendingEscrituraExcel();
        var pending = allPending.Where(r => r.SourceSheet is not null && r.SourceRowNumber is not null).ToList();

        foreach (var orphan in allPending.Where(r => r.SourceSheet is null || r.SourceRowNumber is null))
        {
            result.Alerts.Add($"Caso '{orphan.NombreCompleto}' (RUT {orphan.Rut}) no vino de la matriz — no se puede ubicar fila para actualizar el Excel.");
        }

        if (pending.Count == 0)
        {
            return;
        }

        // Backup must happen before the file is opened for writing below — File.Copy on a
        // source that our own process already holds open for edit fails with IOException on
        // Windows (the open handle, not just external locks, blocks the copy).
        try
        {
            BackupBeforeSave(workbookPath);
        }
        catch (IOException)
        {
            result.ExcelLocked = true;
            result.Alerts.Add("No se pudo respaldar el Excel antes de escribir (archivo bloqueado) — no se realizo ningun cambio.");
            return;
        }

        SpreadsheetDocument? document = null;
        for (var attempt = 1; attempt <= MaxRetries && document is null; attempt++)
        {
            try
            {
                // AutoSave is on by default — SpreadsheetDocument would persist any in-memory
                // mutation to disk on Dispose() even if Save() is never called explicitly
                // (confirmed: an earlier bug here backed up AFTER opening the doc, threw, and
                // the exception unwind still wrote the half-finished edit via autosave). Turning
                // it off means disk is only ever touched by our own explicit Save() call below.
                document = SpreadsheetDocument.Open(workbookPath, isEditable: true, new OpenSettings { AutoSave = false });
            }
            catch (IOException) when (attempt < MaxRetries)
            {
                Thread.Sleep(RetryDelay);
            }
            catch (IOException)
            {
                result.ExcelLocked = true;
                return;
            }
        }

        if (document is null)
        {
            result.ExcelLocked = true;
            return;
        }

        using (document)
        {
            var workbookPart = document.WorkbookPart!;
            var sharedStrings = workbookPart.GetPartsOfType<SharedStringTablePart>().FirstOrDefault();
            var columnsBySheet = new Dictionary<string, MatrizColumns>();
            var writtenIds = new List<long>();

            foreach (var request in pending)
            {
                var sheet = workbookPart.Workbook.Sheets!.Elements<Sheet>()
                    .FirstOrDefault(s => string.Equals(s.Name!.Value, request.SourceSheet, StringComparison.OrdinalIgnoreCase));
                if (sheet is null)
                {
                    result.Alerts.Add($"Hoja '{request.SourceSheet}' ya no existe en el Excel — RUT {request.Rut} no actualizado.");
                    continue;
                }

                var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id!.Value!);
                var sheetData = worksheetPart.Worksheet.GetFirstChild<SheetData>();
                if (sheetData is null)
                {
                    continue;
                }

                if (!columnsBySheet.TryGetValue(sheet.Name!.Value!, out var columns))
                {
                    var mapped = MapColumns(sheetData, sharedStrings);
                    if (mapped is null)
                    {
                        result.Alerts.Add($"No se reconocieron las columnas de '{sheet.Name!.Value}' — RUT {request.Rut} no actualizado.");
                        continue;
                    }
                    columns = mapped;
                    columnsBySheet[sheet.Name!.Value!] = columns;
                }

                // Compare through Rut.TryParse on both sides, not raw string equality — some
                // cases were stored before the leading-zero canonical form existed (their
                // request.Rut is still e.g. "9629999-0" instead of "09629999-0"), which would
                // otherwise never match the Excel's own (correctly padded) cell.
                var requestRutCanonical = Rut.TryParse(request.Rut, out var parsedRequestRut) ? parsedRequestRut.ToString() : request.Rut;

                var rowNumber = request.SourceRowNumber!.Value;
                var row = sheetData.Elements<Row>().FirstOrDefault(r => r.RowIndex!.Value == (uint)rowNumber);

                var currentRutRaw = row is null ? "" : GetCellText(row, columns.Rut, sharedStrings)?.Trim() ?? "";
                var rowMatchesRut = Rut.TryParse(currentRutRaw, out var rowRut) && rowRut.ToString() == requestRutCanonical;

                if (!rowMatchesRut)
                {
                    // The stored row number was captured at import time — rows shift whenever
                    // someone inserts/deletes rows in the live shared spreadsheet since then.
                    // Before giving up, re-locate the case by scanning the whole sheet for its
                    // RUT rather than trusting a now-possibly-stale row number.
                    var relocated = sheetData.Elements<Row>().FirstOrDefault(r =>
                        Rut.TryParse(GetCellText(r, columns.Rut, sharedStrings), out var candidateRut) &&
                        candidateRut.ToString() == requestRutCanonical);

                    if (relocated is null)
                    {
                        result.Alerts.Add($"No se encontro el RUT {request.Rut} en '{sheet.Name!.Value}' (fila {rowNumber} ahora tiene otro caso) — revisar manualmente.");
                        continue;
                    }

                    result.Alerts.Add($"RUT {request.Rut} se movio de la fila {rowNumber} a la fila {relocated.RowIndex!.Value} en '{sheet.Name!.Value}' — actualizado igual, se detecto y siguio el cambio.");
                    row = relocated;
                }

                SetCellText(row!, columns.EstadoCarpeta, EstadoSubidaConF8);
                if (columns.FechaSubioCarpeta != 0)
                {
                    var fecha = (request.FechaDeSubida ?? DateOnly.FromDateTime(DateTime.Today)).ToString("dd/MM/yyyy");
                    SetCellText(row!, columns.FechaSubioCarpeta, fecha);
                }

                writtenIds.Add(request.Id);
                result.RowsWrittenBack++;
            }

            if (writtenIds.Count == 0)
            {
                return;
            }

            var saved = false;
            for (var attempt = 1; attempt <= MaxRetries && !saved; attempt++)
            {
                try
                {
                    foreach (var worksheetPart in workbookPart.GetPartsOfType<WorksheetPart>())
                    {
                        worksheetPart.Worksheet.Save();
                    }
                    document.Save();
                    saved = true;
                }
                catch (IOException) when (attempt < MaxRetries)
                {
                    Thread.Sleep(RetryDelay);
                }
            }

            if (saved)
            {
                foreach (var id in writtenIds)
                {
                    repository.SetPendienteEscrituraExcel(id, false);
                }
            }
            else
            {
                result.ExcelLocked = true;
                result.RowsWrittenBack = 0;
                result.Alerts.Add("No se pudo guardar el Excel (archivo bloqueado) — los casos quedan pendientes, se reintentara en la proxima sincronizacion.");
            }
        }
    }

    private static void BackupBeforeSave(string workbookPath)
    {
        var backupDir = Path.Combine(Path.GetDirectoryName(workbookPath)!, "backups");
        Directory.CreateDirectory(backupDir);
        var backupName = $"{Path.GetFileNameWithoutExtension(workbookPath)}_{DateTime.Now:yyyyMMdd_HHmmss}{Path.GetExtension(workbookPath)}";
        File.Copy(workbookPath, Path.Combine(backupDir, backupName), overwrite: false);
    }

    private static string? SectorFor(string sheetName)
    {
        var normalized = NormalizeHeader(sheetName);
        if (normalized.Contains("PLANTILLA") || normalized.Contains("ESCANEADAS") || normalized.Contains("CORREOS"))
        {
            return null;
        }
        return KnownSectors.FirstOrDefault(sector => normalized.Contains(NormalizeHeader(sector)));
    }

    private sealed record MatrizColumns(int Rut, int NombreCompleto, int EstadoCarpeta, int? FechaCitacion, int FechaSubioCarpeta);

    private static MatrizColumns? MapColumns(SheetData sheetData, SharedStringTablePart? sharedStrings)
    {
        var headerRow = sheetData.Elements<Row>().FirstOrDefault(r => r.RowIndex!.Value == 2);
        if (headerRow is null)
        {
            return null;
        }

        int rutCol = 0, nombreCol = 0, estadoCol = 0, fechaSubioCol = 0, fechaCitacionCol = 0;

        foreach (var cell in headerRow.Elements<Cell>())
        {
            var colIndex = ColumnIndexFromReference(cell.CellReference!.Value!);
            var header = NormalizeHeader(GetCellText(cell, sharedStrings));

            if (header == "RUT")
            {
                rutCol = colIndex;
            }
            else if (header == "NOMBRE COMPLETO")
            {
                nombreCol = colIndex;
            }
            else if (header.Contains("ESTADO") && header.Contains("CARPETA"))
            {
                estadoCol = colIndex;
            }
            else if (header.Contains("FECHA") && header.Contains("SUBIO"))
            {
                fechaSubioCol = colIndex;
            }
            else if (header.Contains("FECHA") && header.Contains("CITACION"))
            {
                fechaCitacionCol = colIndex;
            }
        }

        if (rutCol == 0 || nombreCol == 0 || estadoCol == 0)
        {
            return null;
        }

        return new MatrizColumns(rutCol, nombreCol, estadoCol, fechaCitacionCol == 0 ? null : fechaCitacionCol, fechaSubioCol);
    }

    private static string? GetCellText(Row row, int columnIndex, SharedStringTablePart? sharedStrings)
    {
        var columnLetter = ColumnLetterFromIndex(columnIndex);
        var cell = row.Elements<Cell>().FirstOrDefault(c => ColumnPartOf(c.CellReference!.Value!) == columnLetter);
        return GetCellText(cell, sharedStrings);
    }

    private static string? GetCellText(Cell? cell, SharedStringTablePart? sharedStrings)
    {
        if (cell is null)
        {
            return null;
        }

        if (cell.DataType?.Value == CellValues.InlineString)
        {
            return cell.InlineString?.Text?.InnerText;
        }

        if (cell.DataType?.Value == CellValues.SharedString)
        {
            if (sharedStrings is null || cell.CellValue is null || !int.TryParse(cell.CellValue.InnerText, out var index))
            {
                return null;
            }
            var items = sharedStrings.SharedStringTable.Elements<SharedStringItem>().ToList();
            return index >= 0 && index < items.Count ? items[index].InnerText : null;
        }

        return cell.CellValue?.InnerText;
    }

    private static void SetCellText(Row row, int columnIndex, string value)
    {
        var columnLetter = ColumnLetterFromIndex(columnIndex);
        var cellReference = $"{columnLetter}{row.RowIndex!.Value}";
        var cell = row.Elements<Cell>().FirstOrDefault(c => c.CellReference!.Value == cellReference);

        if (cell is null)
        {
            cell = new Cell { CellReference = cellReference };
            var following = row.Elements<Cell>()
                .FirstOrDefault(c => ColumnIndexFromReference(c.CellReference!.Value!) > columnIndex);
            if (following is not null)
            {
                row.InsertBefore(cell, following);
            }
            else
            {
                row.AppendChild(cell);
            }
        }

        cell.RemoveAllChildren<CellValue>();
        cell.RemoveAllChildren<InlineString>();
        cell.DataType = new EnumValue<CellValues>(CellValues.InlineString);
        cell.AppendChild(new InlineString(new Text(value)));
    }

    private static string ColumnPartOf(string cellReference) => new(cellReference.TakeWhile(char.IsLetter).ToArray());

    private static int ColumnIndexFromReference(string cellReference)
    {
        var index = 0;
        foreach (var ch in ColumnPartOf(cellReference))
        {
            index = index * 26 + (ch - 'A' + 1);
        }
        return index;
    }

    private static string ColumnLetterFromIndex(int index)
    {
        var builder = new StringBuilder();
        while (index > 0)
        {
            var remainder = (index - 1) % 26;
            builder.Insert(0, (char)('A' + remainder));
            index = (index - 1) / 26;
        }
        return builder.ToString();
    }

    private static string NormalizeHeader(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                builder.Append(ch);
            }
        }
        return builder.ToString();
    }
}
