using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using LicenciasCarpetas.CambioDomicilio.Data;
using LicenciasCarpetas.CambioDomicilio.Domain;

namespace LicenciasCarpetas.Dashboard.Pages.CambioDomicilio.Solicitar;

/// <summary>Worklist for outbound "solicitud de cambio de domicilio" requests — the mirror
/// direction of the module's Index (which tracks requests other comunas send TO Valparaíso):
/// here Valparaíso builds and sends its own requests to other comunas.</summary>
[Authorize(Policy = "CambioDomicilioAccess")]
public sealed class IndexModel(IOutboundAddressChangeRequestRepository repository) : PageModel
{
    [TempData(Key = "SolicitarMessage")]
    public string? Message { get; set; }

    public IReadOnlyList<OutboundAddressChangeRequest> Requests { get; private set; } = [];

    public void OnGet() => Requests = repository.GetAll();
}
