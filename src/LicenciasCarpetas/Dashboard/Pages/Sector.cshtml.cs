using LicenciasCarpetas.Domain;
using LicenciasCarpetas.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LicenciasCarpetas.Dashboard.Pages;

[Authorize]
public class SectorModel(IFolderCaseRepository cases) : PageModel
{
    public IReadOnlyList<FolderCase> Cases { get; private set; } = [];

    public FolderSector SelectedSector { get; private set; } = FolderSector.Archivo;

    /// <summary>Only the cases the operator ticked — the usual way to build the list of folders to
    /// physically pull. Unticked lists are available too, for a full sector inventory.</summary>
    public bool OnlyMarked { get; private set; } = true;

    /// <summary>Include folders already requested on an earlier list. Off by default, so a reprint
    /// does not ask Archivo for the same folder twice.</summary>
    public bool IncludePrinted { get; private set; }

    public string? Message { get; private set; }

    /// <summary>Who is signing the document and when — printed in the header of the report.</summary>
    public string OperatorName => User.Identity?.Name ?? "—";

    public DateOnly IssuedOn => DateOnly.FromDateTime(DateTime.Today);

    public void OnGet(string? sector, bool onlyMarked = true, bool includePrinted = false)
    {
        Load(sector, onlyMarked, includePrinted);
    }

    /// <summary>
    /// Records the listed folders as requested, so the next document only brings what is still
    /// pending. Deliberately a separate button from printing: the browser cannot tell this page
    /// whether the print dialog ended in paper or in "cancel".
    /// </summary>
    public IActionResult OnPostMarkPrinted(string? sector, bool onlyMarked = true, bool includePrinted = false)
    {
        Load(sector, onlyMarked, includePrinted);

        var ids = Cases.Select(item => item.Id).ToList();
        cases.MarkSectorPrinted(ids);

        if (TempData is not null)
        {
            TempData["Message"] = $"{ids.Count} carpeta(s) quedaron registradas como pedidas.";
        }

        return RedirectToPage(new { sector = SelectedSector.ToString(), onlyMarked, includePrinted });
    }

    public IActionResult OnPostRequestAgain(long id, string? sector, bool onlyMarked = true)
    {
        cases.ClearSectorPrinted(id);
        if (TempData is not null)
        {
            TempData["Message"] = "La carpeta vuelve a la lista de pendientes.";
        }

        return RedirectToPage(new { sector, onlyMarked, includePrinted = true });
    }

    private void Load(string? sector, bool onlyMarked, bool includePrinted)
    {
        SelectedSector = Enum.TryParse<FolderSector>(sector, ignoreCase: true, out var parsed)
            ? parsed
            : FolderSector.Archivo;
        OnlyMarked = onlyMarked;
        IncludePrinted = includePrinted;
        Cases = cases.ForSector(SelectedSector, onlyMarked, includePrinted);
        Message = TempData?["Message"] as string;
    }

    public static string SectorTitle(FolderSector sector) => FolderSectorCatalog.Display(sector);
}
