using Microsoft.AspNetCore.Http;

namespace LicenciasCarpetas.Dashboard.Pages.Shared;

/// <summary>Which module the current request is in, used by _Layout.cshtml for both the brand
/// title text and which "Módulos" sidebar entry gets marked active — one source of truth so the
/// two can't drift apart the way two independent StartsWith checks could.</summary>
public readonly record struct BrandNavigation(
    string Title, bool EnCambioDomicilio, bool EnF8, bool EnGestionLicencias, string EstadisticasPage)
{
    /// <summary>StartsWithSegments requires a segment boundary (/F8 or /F8/algo, never
    /// /F8Reportes), unlike a plain StartsWith on the raw path string.</summary>
    public static BrandNavigation Resolve(PathString path)
    {
        var enCambioDomicilio = path.StartsWithSegments("/CambioDomicilio");
        var enF8 = path.StartsWithSegments("/F8");
        var enGestionLicencias = !enCambioDomicilio && !enF8;
        var title = enCambioDomicilio ? "Cambio de Domicilio" : enF8 ? "F8 Urgentes" : "Gestión de Licencias";
        // Cada módulo tiene su propia pantalla de Estadísticas — el link de la barra sigue al
        // módulo activo en vez de apuntar siempre a la de Casos.
        var estadisticasPage = enCambioDomicilio ? "/CambioDomicilio/Estadisticas"
            : enF8 ? "/F8/Estadisticas"
            : "/Estadisticas";
        return new BrandNavigation(title, enCambioDomicilio, enF8, enGestionLicencias, estadisticasPage);
    }
}
