using LicenciasCarpetas.Configuration;
using LicenciasCarpetas.Import;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LicenciasCarpetas.Dashboard.Pages;

[Authorize(Roles = "Administrador,Jefatura")]
public class ImportarModel(IExcelWorkbookImporter importer, CarpetasOptions options) : PageModel
{
    public string UploadDirectory => options.UploadDirectory;

    [BindProperty]
    public IFormFile? UploadFile { get; set; }

    /// <summary>Picks a workbook already sitting in the upload folder — dropped there by hand,
    /// synced from Drive, or left over from a previous upload — without re-uploading it.</summary>
    [BindProperty]
    public string? ExistingFileName { get; set; }

    public IReadOnlyList<string> ExistingFiles { get; private set; } = [];

    public ImportSummary? Summary { get; private set; }

    public string? ErrorMessage { get; private set; }

    public void OnGet()
    {
        ExistingFiles = ListWorkbooks();
    }

    public IActionResult OnPost()
    {
        ExistingFiles = ListWorkbooks();

        string path;
        try
        {
            path = ResolveUploadedPath();
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = ex.Message;
            return Page();
        }

        try
        {
            Summary = importer.Import(path);
        }
        catch (FileNotFoundException)
        {
            ErrorMessage = $"No se encontró el archivo: {path}";
        }
        catch (IOException ex)
        {
            // Typically the workbook being synced by Drive or locked by another process.
            ErrorMessage = $"No se pudo leer el archivo: {ex.Message}";
        }

        return Page();
    }

    private string ResolveUploadedPath()
    {
        if (UploadFile is { Length: > 0 })
        {
            if (!Path.GetExtension(UploadFile.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Solo se aceptan archivos .xlsx.");
            }

            Directory.CreateDirectory(UploadDirectory);
            var safeName = Path.GetFileName(UploadFile.FileName);
            var destination = Path.Combine(UploadDirectory, safeName);

            using (var stream = new FileStream(destination, FileMode.Create, FileAccess.Write))
            {
                UploadFile.CopyTo(stream);
            }

            ExistingFiles = ListWorkbooks();
            return destination;
        }

        if (!string.IsNullOrWhiteSpace(ExistingFileName))
        {
            var candidate = Path.Combine(UploadDirectory, Path.GetFileName(ExistingFileName));
            if (!System.IO.File.Exists(candidate))
            {
                throw new InvalidOperationException($"No se encontró el archivo: {candidate}");
            }

            return candidate;
        }

        throw new InvalidOperationException("Suba un archivo .xlsx o elija uno de la lista.");
    }

    private List<string> ListWorkbooks()
    {
        if (!Directory.Exists(UploadDirectory))
        {
            return [];
        }

        return Directory.EnumerateFiles(UploadDirectory, "*.xlsx")
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Select(name => name!)
            .OrderByDescending(name => name)
            .ToList();
    }
}
