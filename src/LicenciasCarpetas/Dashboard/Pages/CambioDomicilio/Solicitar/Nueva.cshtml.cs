using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using LicenciasCarpetas.CambioDomicilio.Data;
using LicenciasCarpetas.CambioDomicilio.Domain;
using LicenciasCarpetas.CambioDomicilio.Extraction;
using LicenciasCarpetas.CambioDomicilio.Routing;
using LicenciasCarpetas.CambioDomicilio.Solicitar;
using LicenciasCarpetas.Persistence;

namespace LicenciasCarpetas.Dashboard.Pages.CambioDomicilio.Solicitar;

/// <summary>Create-or-resume-draft form for one outbound "solicitud de cambio de domicilio":
/// the operator fills in the contributor's data and new address, and finally sends it to the
/// destination comuna's contact email(s) — mirroring the confirmation flow in
/// AddressChangeRoutingService, but for requests Valparaíso itself initiates.</summary>
[Authorize(Policy = "CambioDomicilioAccess")]
public sealed class NuevaModel(
    IOutboundAddressChangeRequestRepository repository,
    AddressChangeRoutingService routingService,
    OutboundRequestSender sender) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public long? RequestId { get; private set; }
    public IReadOnlyList<ComunaRoutingEntry> ComunaOptions { get; private set; } = [];
    public bool ReadOnly { get; private set; }
    public string? Message { get; set; }
    public bool MessageIsError { get; set; }

    public sealed class InputModel
    {
        [Required]
        public string? FullName { get; set; }
        [Required]
        public string? Rut { get; set; }
        public string? Phone { get; set; }
        public string? Street { get; set; }
        public string? Number { get; set; }
        public string? Unit { get; set; }
        [Required]
        public string? DestinationComuna { get; set; }
    }

    public void OnGet(long? id)
    {
        ComunaOptions = LoadComunaOptions();

        var existing = id is { } requestId ? repository.FindById(requestId) : null;
        if (existing is null)
        {
            RequestId = null;
            Input = new InputModel();
            ReadOnly = false;
            return;
        }

        RequestId = existing.Id;
        Input = new InputModel
        {
            FullName = existing.FullName,
            Rut = existing.Rut,
            Phone = existing.Phone,
            Street = existing.Street,
            Number = existing.Number,
            Unit = existing.Unit,
            DestinationComuna = existing.DestinationComuna
        };
        ReadOnly = existing.Status == OutboundRequestStatus.Enviada;
    }

    public IActionResult OnPostGuardarBorrador(long? id)
    {
        ComunaOptions = LoadComunaOptions();

        if (!ModelState.IsValid)
        {
            Message = "Complete los campos obligatorios.";
            MessageIsError = true;
            LoadContext(id);
            return Page();
        }

        var normalizedRut = RutValidator.NormalizeAndValidate(Input.Rut);
        if (normalizedRut is null)
        {
            Message = "El RUT ingresado no es válido (revise el dígito verificador).";
            MessageIsError = true;
            LoadContext(id);
            return Page();
        }

        var savedId = SaveDraft(id, normalizedRut);
        return RedirectToPage(new { id = savedId, saved = true });
    }

    public async Task<IActionResult> OnPostEnviar(long id)
    {
        ComunaOptions = LoadComunaOptions();

        var request = repository.FindById(id);
        if (request is null || request.Status != OutboundRequestStatus.Borrador)
        {
            Message = "La solicitud no existe o ya fue enviada.";
            MessageIsError = true;
            LoadContext(id);
            return Page();
        }

        // This handler's form only posts the id, not the full field set, so the source of truth
        // here is what was already persisted as the draft — re-running ModelState on Input would
        // validate whatever happens to be bound (which may be stale/blank), not the actual saved
        // request that's about to be emailed.
        if (string.IsNullOrWhiteSpace(request.FullName) || string.IsNullOrWhiteSpace(request.Rut)
            || string.IsNullOrWhiteSpace(request.DestinationComuna))
        {
            Message = "Complete todos los campos obligatorios antes de enviar.";
            MessageIsError = true;
            LoadContext(id);
            return Page();
        }

        var attachments = repository.GetAttachments(id);
        var userId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await sender.SendAsync(request, attachments, userId);

        switch (result.Outcome)
        {
            case OutboundSendOutcome.NoContact:
                Message = $"No hay correo de contacto registrado para '{result.DestinationComuna}'. Agréguelo en Comunas antes de enviar.";
                MessageIsError = true;
                LoadContext(id);
                return Page();
            case OutboundSendOutcome.SendFailed:
                Message = "No se pudo enviar el correo (revise la configuración SMTP o la conexión). La solicitud sigue como Borrador — puede reintentar.";
                MessageIsError = true;
                LoadContext(id);
                return Page();
            default:
                TempData["SolicitarMessage"] = result.Outcome == OutboundSendOutcome.Sent
                    ? $"Solicitud enviada a {result.DestinationComuna}."
                    : "El correo se envió, pero la solicitud ya figuraba como enviada (posiblemente por otra pestaña/operador).";
                return RedirectToPage("/CambioDomicilio/Solicitar/Index");
        }
    }

    private long SaveDraft(long? id, string normalizedRut)
    {
        if (id is { } existingId)
        {
            var existing = repository.FindById(existingId);
            if (existing is not null && existing.Status == OutboundRequestStatus.Borrador)
            {
                existing.FullName = Input.FullName!.Trim();
                existing.Rut = normalizedRut;
                existing.Phone = string.IsNullOrWhiteSpace(Input.Phone) ? null : Input.Phone.Trim();
                existing.Street = string.IsNullOrWhiteSpace(Input.Street) ? null : Input.Street.Trim();
                existing.Number = string.IsNullOrWhiteSpace(Input.Number) ? null : Input.Number.Trim();
                existing.Unit = string.IsNullOrWhiteSpace(Input.Unit) ? null : Input.Unit.Trim();
                existing.DestinationComuna = Input.DestinationComuna!.Trim();
                repository.Update(existing);
                return existingId;
            }
        }

        return repository.Insert(new OutboundAddressChangeRequest
        {
            FullName = Input.FullName!.Trim(),
            Rut = normalizedRut,
            Phone = string.IsNullOrWhiteSpace(Input.Phone) ? null : Input.Phone.Trim(),
            Street = string.IsNullOrWhiteSpace(Input.Street) ? null : Input.Street.Trim(),
            Number = string.IsNullOrWhiteSpace(Input.Number) ? null : Input.Number.Trim(),
            Unit = string.IsNullOrWhiteSpace(Input.Unit) ? null : Input.Unit.Trim(),
            DestinationComuna = Input.DestinationComuna!.Trim(),
            CreatedByUserId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!)
        });
    }

    private void LoadContext(long? id)
    {
        RequestId = id;
        ReadOnly = id is { } requestId && repository.FindById(requestId)?.Status == OutboundRequestStatus.Enviada;
    }

    private IReadOnlyList<ComunaRoutingEntry> LoadComunaOptions() => routingService.LoadDirectory()
        .DistinctBy(c => c.Comuna, StringComparer.OrdinalIgnoreCase)
        .OrderBy(c => c.Comuna)
        .ToList();
}
