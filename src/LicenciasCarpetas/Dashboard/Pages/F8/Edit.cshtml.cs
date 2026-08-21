using Microsoft.AspNetCore.Authorization;
using System.ComponentModel.DataAnnotations;
using LicenciasCarpetas.F8.Data;
using LicenciasCarpetas.F8.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LicenciasCarpetas.Dashboard.Pages.F8;

[Authorize(Policy = "F8Access")]
public sealed class EditModel(IUrgentRequestRepository repository) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public void OnGet(long? id)
    {
        if (id is null)
        {
            Input = new InputModel();
            return;
        }

        var existing = repository.FindById(id.Value);
        if (existing is null)
        {
            Input = new InputModel();
            return;
        }

        Input = new InputModel
        {
            Id = existing.Id,
            FechaPeticion = existing.FechaPeticion,
            Nombres = existing.Nombres,
            Apellidos = existing.Apellidos,
            NombreCompleto = existing.NombreCompleto,
            Rut = existing.Rut,
            FechaUltimaCarpeta = existing.FechaUltimaCarpeta,
            CodigoF8 = existing.CodigoF8,
            FechaPenultimaCarpeta = existing.FechaPenultimaCarpeta,
            Estado = existing.Estado,
            EstadoActual = existing.EstadoActual,
            FechaDeSubida = existing.FechaDeSubida,
        };
    }

    public IActionResult? OnPost()
    {
        if (!Rut.TryParse(Input.Rut, out var parsedRut) || !parsedRut.IsValid)
        {
            ModelState.AddModelError(nameof(Input.Rut), "RUT inválido: verifique el dígito verificador.");
            return Page();
        }

        UrgentRequest request;
        if (Input.Id is { } id)
        {
            var existing = repository.FindById(id);
            if (existing is null)
            {
                ModelState.AddModelError(string.Empty, "La solicitud no existe.");
                return Page();
            }
            request = existing;
        }
        else
        {
            request = new UrgentRequest { Origin = "Web", SourceSheet = null, CreatedAt = DateTimeOffset.UtcNow };
        }

        request.FechaPeticion = Input.FechaPeticion;
        request.Nombres = Input.Nombres;
        request.Apellidos = Input.Apellidos;
        request.NombreCompleto = Input.NombreCompleto;
        request.Rut = parsedRut.ToString();
        request.RutRaw = Input.Rut;
        request.FechaUltimaCarpeta = Input.FechaUltimaCarpeta;
        request.CodigoF8 = Input.CodigoF8;
        request.FechaPenultimaCarpeta = Input.FechaPenultimaCarpeta;
        var estadoValue = EstadoCatalog.NormalizeForPersistence(Input.Estado, isEstado: true);
        var estadoActualValue = EstadoCatalog.NormalizeForPersistence(Input.EstadoActual, isEstado: false);
        var unknownEstado = EstadoCatalog.IsUnknownEstado(Input.Estado);
        var unknownEstadoActual = EstadoCatalog.IsUnknownEstadoActual(Input.EstadoActual);
        request.Estado = estadoValue;
        request.EstadoActual = estadoActualValue;
        request.FechaDeSubida = Input.FechaDeSubida;

        var shouldClearReview = Input.Id is not null && !unknownEstado && !unknownEstadoActual;
        if (shouldClearReview)
        {
            request.NeedsReview = false;
        }
        else
        {
            request.NeedsReview = request.NeedsReview || unknownEstado || unknownEstadoActual;
        }

        if (Input.Id is null)
        {
            var insertedId = repository.Insert(request);
            if (unknownEstado)
            {
                repository.AddFlag(insertedId, "Estado", ImportFlag.ReasonCodes.UnknownEstado, Input.Estado);
            }
            if (unknownEstadoActual)
            {
                repository.AddFlag(insertedId, "EstadoActual", ImportFlag.ReasonCodes.UnknownEstadoActual, Input.EstadoActual);
            }
        }
        else
        {
            repository.Update(request);
            if (shouldClearReview)
            {
                repository.ClearFlags(request.Id);
            }
            if (unknownEstado)
            {
                repository.AddFlag(request.Id, "Estado", ImportFlag.ReasonCodes.UnknownEstado, Input.Estado);
            }
            if (unknownEstadoActual)
            {
                repository.AddFlag(request.Id, "EstadoActual", ImportFlag.ReasonCodes.UnknownEstadoActual, Input.EstadoActual);
            }
        }

        return RedirectToPage("Index");
    }

    public sealed class InputModel
    {
        public long? Id { get; set; }
        public DateOnly? FechaPeticion { get; set; }
        public string? Nombres { get; set; }
        public string? Apellidos { get; set; }
        [Required]
        public string? NombreCompleto { get; set; }
        [Required]
        public string? Rut { get; set; }
        public DateOnly? FechaUltimaCarpeta { get; set; }
        public string? CodigoF8 { get; set; }
        public DateOnly? FechaPenultimaCarpeta { get; set; }
        public string? Estado { get; set; }
        public string? EstadoActual { get; set; }
        public DateOnly? FechaDeSubida { get; set; }
    }
}
