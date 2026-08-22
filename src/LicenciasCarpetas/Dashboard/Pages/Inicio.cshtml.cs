using LicenciasCarpetas.CambioDomicilio.Data;
using LicenciasCarpetas.CambioDomicilio.Statistics;
using LicenciasCarpetas.F8.Data;
using LicenciasCarpetas.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LicenciasCarpetas.Dashboard.Pages;

/// <summary>Landing hub reached from the sidebar's "Panel de Control" link — one card per módulo
/// with a live count, built entirely from the same repository calls each módulo's propia
/// Estadísticas page already uses. No new queries: this page only decides which of them to run.</summary>
[Authorize]
public class InicioModel(
    IFolderCaseRepository cases,
    ICambioDomicilioRequestRepository cambioDomicilioRequests,
    CambioDomicilioStatisticsService cambioDomicilioStatistics,
    IUrgentRequestRepository urgentRequests) : PageModel
{
    private const string F8EstadoActualSubida = "SUBIDA A CONASET";

    public int CasosCount { get; private set; }

    public bool PuedeCambioDomicilio { get; private set; }
    public int? CambioDomicilioPendientes { get; private set; }

    public bool PuedeF8 { get; private set; }
    public int? F8Count { get; private set; }

    public void OnGet()
    {
        // Mismo total que /Index usa para su paginación: sin filtro, cuenta todos los casos.
        CasosCount = cases.Count(new CaseFilter());

        PuedeCambioDomicilio = User.HasClaim("mod:cambio-domicilio", "true");
        if (PuedeCambioDomicilio)
        {
            var requests = cambioDomicilioRequests.GetAll();
            CambioDomicilioPendientes = cambioDomicilioStatistics.GetStatusCounts(requests).Pending;
        }

        PuedeF8 = User.HasClaim("mod:f8-urgentes", "true");
        if (PuedeF8)
        {
            var all = urgentRequests.GetAll();
            F8Count = all.Count(r => r.EstadoActual != F8EstadoActualSubida);
        }
    }
}
