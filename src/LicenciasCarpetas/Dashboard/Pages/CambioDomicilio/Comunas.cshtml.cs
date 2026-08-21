using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using LicenciasCarpetas.CambioDomicilio;
using LicenciasCarpetas.CambioDomicilio.Directories;
using LicenciasCarpetas.CambioDomicilio.Domain;

namespace LicenciasCarpetas.Dashboard.Pages.CambioDomicilio;

[Authorize(Policy = "CambioDomicilioAccess")]
public class ComunasModel(IComunaDirectory directory, CambioDomicilioOptions options) : PageModel
{
    public IReadOnlyList<ComunaRoutingEntry> Contacts { get; private set; } = [];
    public string? Message { get; set; }
    public bool MessageIsError { get; set; }

    public void OnGet() => Load();

    public IActionResult OnPostUpdateEmail(string comuna, string email)
    {
        if (directory.UpdateContactEmail(options.ComunaDirectoryCsvPath ?? string.Empty, comuna, email))
        {
            Message = $"Correo de {comuna} actualizado. Los próximos envíos usarán la nueva dirección.";
        }
        else
        {
            Message = $"No se pudo actualizar {comuna}: revise que el correo tenga un formato válido.";
            MessageIsError = true;
        }

        Load();
        return Page();
    }

    public IActionResult OnPostAddContact(string comuna, string domain, string email)
    {
        if (directory.AddContact(options.ComunaDirectoryCsvPath ?? string.Empty, comuna, email, domain))
        {
            Message = $"Comuna {comuna} agregada al directorio.";
        }
        else
        {
            Message = "No se pudo agregar: revise que la comuna, el dominio y el correo tengan un formato válido.";
            MessageIsError = true;
        }

        Load();
        return Page();
    }

    private void Load() =>
        Contacts = directory.LoadFromCsv(options.ComunaDirectoryCsvPath ?? string.Empty)
            .OrderBy(c => c.Comuna)
            .ToList();
}
