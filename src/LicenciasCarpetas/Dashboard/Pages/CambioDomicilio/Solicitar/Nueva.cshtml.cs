using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using LicenciasCarpetas.CambioDomicilio.Data;
using LicenciasCarpetas.CambioDomicilio.Domain;
using LicenciasCarpetas.CambioDomicilio.Extraction;
using LicenciasCarpetas.CambioDomicilio.Routing;
using LicenciasCarpetas.Configuration;
using LicenciasCarpetas.F8.Services;
using LicenciasCarpetas.Persistence;

namespace LicenciasCarpetas.Dashboard.Pages.CambioDomicilio.Solicitar;

/// <summary>Create-or-resume-draft form for one outbound "solicitud de cambio de domicilio":
/// the operator fills in the contributor's data and new address, attaches supporting documents,
/// and finally sends it to the destination comuna's contact email(s) — mirroring the confirmation
/// flow in AddressChangeRoutingService, but for requests Valparaíso itself initiates.</summary>
[Authorize(Policy = "CambioDomicilioAccess")]
[RequestFormLimits(MultipartBodyLengthLimit = 6_000_000)]
public sealed class NuevaModel(
    IOutboundAddressChangeRequestRepository repository,
    IComunaContactRepository comunaContactRepository,
    AddressChangeRoutingService routingService,
    IEmailSender emailSender,
    CarpetasOptions carpetasOptions) : PageModel
{
    private static readonly string[] AllowedExtensions = [".pdf", ".jpg", ".jpeg"];
    private const long MaxAttachmentSizeBytes = 5 * 1024 * 1024;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty]
    public IFormFile? NewAttachment { get; set; }

    public long? RequestId { get; private set; }
    public IReadOnlyList<OutboundAddressChangeAttachment> Attachments { get; private set; } = [];
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
        [Required]
        public string? Street { get; set; }
        [Required]
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
            Attachments = [];
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
        Attachments = repository.GetAttachments(existing.Id);
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

    public async Task<IActionResult> OnPostSubirArchivo(long? id)
    {
        ComunaOptions = LoadComunaOptions();

        long requestId;
        if (id is { } existingId)
        {
            var existingRequest = repository.FindById(existingId);
            if (existingRequest is null || existingRequest.Status != OutboundRequestStatus.Borrador)
            {
                Message = "La solicitud no existe o ya fue enviada.";
                MessageIsError = true;
                LoadContext(existingId);
                return Page();
            }

            requestId = existingId;
        }
        else
        {
            // No draft saved yet (a brand-new request) — save the current form data first so
            // there is a RequestId to attach the file to, same fields OnPostGuardarBorrador saves.
            if (!ModelState.IsValid)
            {
                Message = "Complete los campos obligatorios antes de adjuntar un documento.";
                MessageIsError = true;
                LoadContext(null);
                return Page();
            }

            var normalizedRut = RutValidator.NormalizeAndValidate(Input.Rut);
            if (normalizedRut is null)
            {
                Message = "El RUT ingresado no es válido (revise el dígito verificador).";
                MessageIsError = true;
                LoadContext(null);
                return Page();
            }

            requestId = SaveDraft(null, normalizedRut);
        }

        if (NewAttachment is not { Length: > 0 })
        {
            Message = "Seleccione un archivo para adjuntar.";
            MessageIsError = true;
            LoadContext(requestId);
            return Page();
        }

        var extension = Path.GetExtension(NewAttachment.FileName);
        if (!AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            Message = "Solo se aceptan archivos .pdf, .jpg o .jpeg.";
            MessageIsError = true;
            LoadContext(requestId);
            return Page();
        }

        using (var signatureStream = NewAttachment.OpenReadStream())
        {
            if (!HasValidSignature(signatureStream, extension))
            {
                Message = "El archivo no parece ser un PDF/JPG válido (revise que no esté corrupto o mal nombrado).";
                MessageIsError = true;
                LoadContext(requestId);
                return Page();
            }
        }

        if (NewAttachment.Length > MaxAttachmentSizeBytes)
        {
            Message = "El archivo supera el tamaño máximo permitido (5 MB).";
            MessageIsError = true;
            LoadContext(requestId);
            return Page();
        }

        var targetDirectory = Path.Combine(carpetasOptions.UploadDirectory, "solicitudes-cambio-domicilio", requestId.ToString());
        var safeFileName = Path.GetFileName(NewAttachment.FileName);
        var destination = Path.Combine(targetDirectory, safeFileName);

        try
        {
            Directory.CreateDirectory(targetDirectory);
            using var stream = new FileStream(destination, FileMode.Create, FileAccess.Write);
            await NewAttachment.CopyToAsync(stream);
        }
        catch (IOException)
        {
            Message = "No se pudo guardar el archivo (revise espacio en disco o permisos).";
            MessageIsError = true;
            LoadContext(requestId);
            return Page();
        }

        repository.AddAttachment(new OutboundAddressChangeAttachment
        {
            RequestId = requestId,
            FileName = safeFileName,
            StoredPath = destination,
            ContentType = NewAttachment.ContentType
        });

        return RedirectToPage(new { id = requestId });
    }

    public IActionResult OnPostEliminarArchivo(long id, long attachmentId)
    {
        var request = repository.FindById(id);
        if (request is null || request.Status != OutboundRequestStatus.Borrador)
        {
            ComunaOptions = LoadComunaOptions();
            Message = "La solicitud no existe o ya fue enviada.";
            MessageIsError = true;
            LoadContext(id);
            return Page();
        }

        var attachment = repository.GetAttachments(id).FirstOrDefault(a => a.Id == attachmentId);
        if (attachment is not null)
        {
            try
            {
                if (System.IO.File.Exists(attachment.StoredPath))
                {
                    System.IO.File.Delete(attachment.StoredPath);
                }
            }
            catch (IOException)
            {
                // A missing/locked file on disk shouldn't block removing the DB row — the
                // operator explicitly asked to remove this attachment from the request.
            }

            repository.DeleteAttachment(attachmentId);
        }

        return RedirectToPage(new { id });
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
            || string.IsNullOrWhiteSpace(request.Street) || string.IsNullOrWhiteSpace(request.Number)
            || string.IsNullOrWhiteSpace(request.DestinationComuna))
        {
            Message = "Complete todos los campos obligatorios antes de enviar.";
            MessageIsError = true;
            LoadContext(id);
            return Page();
        }

        var attachments = repository.GetAttachments(id);
        if (attachments.Count == 0)
        {
            Message = "Debe adjuntar al menos un documento antes de enviar.";
            MessageIsError = true;
            LoadContext(id);
            return Page();
        }

        var contacts = comunaContactRepository.All()
            .Where(c => string.Equals(c.Comuna, request.DestinationComuna, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (contacts.Count == 0)
        {
            Message = $"No hay correo de contacto registrado para '{request.DestinationComuna}'. Agréguelo en Comunas antes de enviar.";
            MessageIsError = true;
            LoadContext(id);
            return Page();
        }

        var subject = $"Solicitud de cambio de domicilio — {request.FullName} ({request.Rut})";
        var direccion = request.Street + " " + request.Number + (string.IsNullOrWhiteSpace(request.Unit) ? string.Empty : $", {request.Unit}");
        var body = $"""
            Estimados,

            Se solicita gestionar el cambio de domicilio para el siguiente contribuyente:

            Nombre: {request.FullName}
            RUT: {request.Rut}
            Nuevo domicilio: {direccion}
            Comuna: {request.DestinationComuna}

            Saludos,
            Cambio de Domicilio
            """;

        var emailAttachments = attachments
            .Select(a => new EmailAttachment(a.FileName, a.ContentType, a.StoredPath))
            .ToList();
        var to = string.Join(";", contacts.Select(c => c.Email));

        try
        {
            await emailSender.SendAsync(to, subject, body, emailAttachments);
        }
        catch (Exception ex) when (ex is System.Net.Mail.SmtpException or System.Net.Sockets.SocketException or InvalidOperationException)
        {
            Console.WriteLine($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] Envío de solicitud de cambio de domicilio #{id} a comuna '{request.DestinationComuna}' falló (SMTP).");
            Message = "No se pudo enviar el correo (revise la configuración SMTP o la conexión). La solicitud sigue como Borrador — puede reintentar.";
            MessageIsError = true;
            LoadContext(id);
            return Page();
        }

        var userId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var sent = repository.MarkSent(id, DateTimeOffset.UtcNow, userId);

        TempData["SolicitarMessage"] = sent
            ? $"Solicitud enviada a {request.DestinationComuna}."
            : "El correo se envió, pero la solicitud ya figuraba como enviada (posiblemente por otra pestaña/operador).";
        return RedirectToPage("/CambioDomicilio/Solicitar/Index");
    }

    private static bool HasValidSignature(Stream stream, string extension)
    {
        var buffer = new byte[8];
        var read = stream.Read(buffer, 0, buffer.Length);
        stream.Position = 0; // rewind so the caller can still copy the full stream afterward
        if (read < 4) return false;

        return extension.ToLowerInvariant() switch
        {
            ".pdf" => buffer[0] == 0x25 && buffer[1] == 0x50 && buffer[2] == 0x44 && buffer[3] == 0x46, // %PDF
            ".jpg" or ".jpeg" => buffer[0] == 0xFF && buffer[1] == 0xD8 && buffer[2] == 0xFF,
            _ => false
        };
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
                existing.Street = Input.Street!.Trim();
                existing.Number = Input.Number!.Trim();
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
            Street = Input.Street!.Trim(),
            Number = Input.Number!.Trim(),
            Unit = string.IsNullOrWhiteSpace(Input.Unit) ? null : Input.Unit.Trim(),
            DestinationComuna = Input.DestinationComuna!.Trim(),
            CreatedByUserId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!)
        });
    }

    private void LoadContext(long? id)
    {
        RequestId = id;
        if (id is { } requestId)
        {
            Attachments = repository.GetAttachments(requestId);
            ReadOnly = repository.FindById(requestId)?.Status == OutboundRequestStatus.Enviada;
        }
        else
        {
            Attachments = [];
            ReadOnly = false;
        }
    }

    private IReadOnlyList<ComunaRoutingEntry> LoadComunaOptions() => routingService.LoadDirectory()
        .DistinctBy(c => c.Comuna, StringComparer.OrdinalIgnoreCase)
        .OrderBy(c => c.Comuna)
        .ToList();
}
