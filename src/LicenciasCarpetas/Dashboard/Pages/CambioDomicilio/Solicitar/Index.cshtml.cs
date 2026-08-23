using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using LicenciasCarpetas.CambioDomicilio.Data;
using LicenciasCarpetas.CambioDomicilio.Domain;
using LicenciasCarpetas.CambioDomicilio.Solicitar;

namespace LicenciasCarpetas.Dashboard.Pages.CambioDomicilio.Solicitar;

/// <summary>Worklist for outbound "solicitud de cambio de domicilio" requests — the mirror
/// direction of the module's Index (which tracks requests other comunas send TO Valparaíso):
/// here Valparaíso builds and sends its own requests to other comunas.</summary>
[Authorize(Policy = "CambioDomicilioAccess")]
public sealed class IndexModel(IOutboundAddressChangeRequestRepository repository, OutboundRequestSender sender) : PageModel
{
    [TempData(Key = "SolicitarMessage")]
    public string? Message { get; set; }

    public IReadOnlyList<OutboundAddressChangeRequest> Requests { get; private set; } = [];

    public void OnGet() => Requests = repository.GetAll();

    /// <summary>Envía una solicitud Borrador directo desde el listado, sin pasar por Nueva.cshtml
    /// — mismo camino que NuevaModel.OnPostEnviar (vía OutboundRequestSender, para no duplicar
    /// la búsqueda de contacto ni el armado del correo), pero un clic más corto para el caso común
    /// de "ya está todo cargado, solo falta apretar enviar".</summary>
    public async Task<IActionResult> OnPostSolicitar(long id)
    {
        var request = repository.FindById(id);
        if (request is null || request.Status != OutboundRequestStatus.Borrador)
        {
            Message = "La solicitud no existe o ya fue enviada.";
            return RedirectToPage();
        }

        if (string.IsNullOrWhiteSpace(request.FullName) || string.IsNullOrWhiteSpace(request.Rut)
            || string.IsNullOrWhiteSpace(request.DestinationComuna))
        {
            Message = "Complete todos los campos obligatorios antes de enviar (editar la solicitud).";
            return RedirectToPage();
        }

        var attachments = repository.GetAttachments(id);
        var userId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await sender.SendAsync(request, attachments, userId);

        Message = result.Outcome switch
        {
            OutboundSendOutcome.NoContact =>
                $"No hay correo de contacto registrado para '{result.DestinationComuna}'. Agréguelo en Comunas antes de enviar.",
            OutboundSendOutcome.SendFailed =>
                "No se pudo enviar el correo (revise la configuración SMTP o la conexión). La solicitud sigue como Borrador — puede reintentar.",
            OutboundSendOutcome.Sent => $"Solicitud enviada a {result.DestinationComuna}.",
            _ => "El correo se envió, pero la solicitud ya figuraba como enviada (posiblemente por otra pestaña/operador)."
        };
        return RedirectToPage();
    }
}
