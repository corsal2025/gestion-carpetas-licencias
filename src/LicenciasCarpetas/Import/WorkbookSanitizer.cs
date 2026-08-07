using System.IO.Compression;
using System.Xml.Linq;

namespace LicenciasCarpetas.Import;

/// <summary>
/// The real "DETALLE CARPETAS" workbook carries dropdown (data validation) lists whose formula is
/// longer than the 255 characters ClosedXML accepts, and it refuses to open the file because of
/// them. Nothing in the import needs validations, so this produces a throwaway copy without them.
/// </summary>
public static class WorkbookSanitizer
{
    /// <summary>Copies the workbook to a temp file and strips every data-validation element from it.
    /// The caller owns the returned file and must delete it.</summary>
    public static string CreateCopyWithoutDataValidations(string workbookPath)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"carpetas-import-{Guid.NewGuid():N}.xlsx");

        // Copied through a shared read stream: the workbook usually lives on a synced Drive folder
        // and may be open in Excel at the same time.
        using (var source = new FileStream(workbookPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var destination = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            source.CopyTo(destination);
        }

        try
        {
            StripDataValidations(tempPath);
            return tempPath;
        }
        catch
        {
            File.Delete(tempPath);
            throw;
        }
    }

    private static void StripDataValidations(string xlsxPath)
    {
        using var archive = ZipFile.Open(xlsxPath, ZipArchiveMode.Update);

        foreach (var entry in archive.Entries.ToList())
        {
            if (!entry.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase)
                || !entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            XDocument document;
            using (var reader = entry.Open())
            {
                document = XDocument.Load(reader);
            }

            var validations = document.Descendants()
                .Where(element => element.Name.LocalName is "dataValidations" or "dataValidation")
                .ToList();

            if (validations.Count == 0)
            {
                continue;
            }

            foreach (var validation in validations)
            {
                validation.Remove();
            }

            using var writer = entry.Open();
            writer.SetLength(0);
            document.Save(writer);
        }
    }
}
