using LicenciasCarpetas.Configuration;
using LicenciasCarpetas.Import;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LicenciasCarpetas.Dashboard.Pages;

[Authorize]
public class ImportarModel(IExcelWorkbookImporter importer, CarpetasOptions options) : PageModel
{
    public string DefaultWorkbookPath => options.DefaultWorkbookPath;

    [BindProperty]
    public string? WorkbookPath { get; set; }

    public ImportSummary? Summary { get; private set; }

    public string? ErrorMessage { get; private set; }

    public void OnGet()
    {
        WorkbookPath = options.DefaultWorkbookPath;
    }

    public IActionResult OnPost()
    {
        var path = string.IsNullOrWhiteSpace(WorkbookPath) ? options.DefaultWorkbookPath : WorkbookPath.Trim();

        if (string.IsNullOrWhiteSpace(path))
        {
            ErrorMessage = "Indique la ruta del archivo Excel.";
            return Page();
        }

        try
        {
            Summary = importer.Import(path);
            WorkbookPath = path;
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
}
