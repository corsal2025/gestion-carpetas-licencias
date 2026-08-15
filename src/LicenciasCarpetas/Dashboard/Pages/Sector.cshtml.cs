using LicenciasCarpetas.Domain;
using LicenciasCarpetas.Persistence;
using Microsoft.AspNetCore.Authorization;
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

    public void OnGet(string? sector, bool onlyMarked = true)
    {
        SelectedSector = Enum.TryParse<FolderSector>(sector, ignoreCase: true, out var parsed)
            ? parsed
            : FolderSector.Archivo;
        OnlyMarked = onlyMarked;
        Cases = cases.ForSector(SelectedSector, onlyMarked);
    }

    public static string SectorTitle(FolderSector sector) => FolderSectorCatalog.Display(sector);
}
