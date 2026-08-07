using System.IO.Compression;
using System.Xml.Linq;
using ClosedXML.Excel;
using LicenciasCarpetas.Import;

namespace LicenciasCarpetas.Tests;

public class WorkbookSanitizerTests
{
    private const string SpreadsheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    /// <summary>
    /// Reproduces what the real workbook has: a dropdown list whose formula is longer than the 255
    /// characters ClosedXML tolerates. It cannot be produced through ClosedXML itself, so it is
    /// injected straight into the sheet XML.
    /// </summary>
    private static string CreateWorkbookWithOversizedValidation()
    {
        var path = Path.Combine(Path.GetTempPath(), $"carpetas-test-{Guid.NewGuid():N}.xlsx");

        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.Worksheets.Add("ENERO AV. ARGENTINA");
            sheet.Cell(1, 1).Value = "FECHA DE LA CITACIÓN";
            workbook.SaveAs(path);
        }

        using (var archive = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            var entry = archive.Entries.First(e => e.FullName.StartsWith("xl/worksheets/", StringComparison.Ordinal));

            XDocument document;
            using (var reader = entry.Open())
            {
                document = XDocument.Load(reader);
            }

            XNamespace ns = SpreadsheetNamespace;
            var longList = string.Join(',', Enumerable.Range(0, 40).Select(i => $"VALOR MUY LARGO NUMERO {i:D3}"));
            document.Root!.Add(new XElement(ns + "dataValidations",
                new XAttribute("count", 1),
                new XElement(ns + "dataValidation",
                    new XAttribute("type", "list"),
                    new XAttribute("sqref", "A1"),
                    new XElement(ns + "formula1", $"\"{longList}\""))));

            using var writer = entry.Open();
            writer.SetLength(0);
            document.Save(writer);
        }

        return path;
    }

    [Fact]
    public void Removes_the_validations_that_stop_the_workbook_from_loading()
    {
        var path = CreateWorkbookWithOversizedValidation();
        string? sanitized = null;

        try
        {
            // Guard: without sanitizing, the loader is the one that fails — not the test setup.
            Assert.ThrowsAny<Exception>(() =>
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var workbook = new XLWorkbook(stream);
            });

            sanitized = WorkbookSanitizer.CreateCopyWithoutDataValidations(path);

            using var sanitizedStream = new FileStream(sanitized, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var loaded = new XLWorkbook(sanitizedStream);

            Assert.Equal("ENERO AV. ARGENTINA", loaded.Worksheets.First().Name);
            Assert.Equal("FECHA DE LA CITACIÓN", loaded.Worksheets.First().Cell(1, 1).GetString());
        }
        finally
        {
            File.Delete(path);
            if (sanitized is not null)
            {
                File.Delete(sanitized);
            }
        }
    }

    [Fact]
    public void The_original_file_is_left_untouched()
    {
        var path = CreateWorkbookWithOversizedValidation();
        var originalBytes = File.ReadAllBytes(path);
        string? sanitized = null;

        try
        {
            sanitized = WorkbookSanitizer.CreateCopyWithoutDataValidations(path);

            Assert.NotEqual(path, sanitized);
            Assert.Equal(originalBytes, File.ReadAllBytes(path));
        }
        finally
        {
            File.Delete(path);
            if (sanitized is not null)
            {
                File.Delete(sanitized);
            }
        }
    }
}
