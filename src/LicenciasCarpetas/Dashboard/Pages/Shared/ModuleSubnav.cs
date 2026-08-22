using Microsoft.AspNetCore.Http;

namespace LicenciasCarpetas.Dashboard.Pages.Shared;

/// <summary>Which pill is "active" in a module's own page-nav bar (_F8Nav.cshtml,
/// _CambioDomicilioNav.cshtml) — was duplicated verbatim as local functions in both partials
/// before this, same drift risk BrandNavigation.cs was extracted to avoid.</summary>
public static class ModuleSubnav
{
    /// <summary>True when the request path is the given page or a sub-route of it
    /// (StartsWithSegments requires a segment boundary, so "/F8" never matches "/F8Reportes").</summary>
    public static bool IsActive(PathString requestPath, string page) => requestPath.StartsWithSegments(page);

    /// <summary>For a page shared by more than one sector link (e.g. "/F8/SectorF8?sector=Archivo"
    /// and "?sector=Oficina43" both route to SectorF8) — active only when both the page and the
    /// sector query value match.</summary>
    public static bool IsSectorActive(PathString requestPath, string sectorPage, string sector, string sectorValue) =>
        requestPath.StartsWithSegments(sectorPage) && sector == sectorValue;
}
