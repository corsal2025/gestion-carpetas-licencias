using LicenciasCarpetas.Dashboard.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LicenciasCarpetas.Dashboard.Pages;

/// <summary>
/// Account management for someone already signed in: create colleagues' accounts and set a new
/// password when one of them forgets theirs. Recovering an account nobody can sign into stays on
/// the console (see /ForgotPassword) — an anonymous web reset would hand the whole agenda to
/// anyone who can reach this port.
/// </summary>
[Authorize]
public class UsuariosModel(IUserRepository users, UserProvisioning provisioning) : PageModel
{
    public IReadOnlyList<string> Usernames { get; private set; } = [];

    public string? CurrentUsername { get; private set; }

    public string? Message { get; set; }
    public bool MessageIsError { get; set; }

    public void OnGet()
    {
        Load();
    }

    public IActionResult OnPostCreate(string? usuario, string? clave, string? confirmacion)
    {
        var result = provisioning.Create(usuario, clave, confirmacion);
        return Finish(result, result == ProvisioningResult.Created
            ? $"Usuario '{usuario?.Trim().ToLowerInvariant()}' creado."
            : UserProvisioning.Describe(result));
    }

    public IActionResult OnPostSetPassword(string? usuario, string? clave, string? confirmacion)
    {
        var result = provisioning.SetPassword(usuario, clave, confirmacion);
        return Finish(result, result == ProvisioningResult.Created
            ? $"Contraseña de '{usuario}' cambiada. La cuenta quedó desbloqueada."
            : UserProvisioning.Describe(result));
    }

    public IActionResult OnPostDelete(string usuario)
    {
        // Deleting the account you are signed in with locks everyone out of a running app.
        if (string.Equals(usuario, User.Identity?.Name, StringComparison.OrdinalIgnoreCase))
        {
            return Finish(ProvisioningResult.UsernameInvalid, "No puede eliminar su propio usuario.");
        }

        if (users.Count() <= 1)
        {
            return Finish(ProvisioningResult.UsernameInvalid, "No puede eliminar el último usuario del sistema.");
        }

        users.Delete(usuario);
        return Finish(ProvisioningResult.Created, $"Usuario '{usuario}' eliminado.");
    }

    private IActionResult Finish(ProvisioningResult result, string message)
    {
        TempData["Message"] = message;
        TempData["MessageIsError"] = result != ProvisioningResult.Created;
        return RedirectToPage();
    }

    private void Load()
    {
        Usernames = users.AllUsernames();
        CurrentUsername = User.Identity?.Name;
        Message = TempData["Message"] as string;
        MessageIsError = TempData["MessageIsError"] is true;
    }
}
