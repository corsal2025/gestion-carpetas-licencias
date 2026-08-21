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
[Authorize(Roles = "Administrador")]
public class UsuariosModel(IUserRepository users, UserProvisioning provisioning) : PageModel
{
    public IReadOnlyList<DashboardUser> Users { get; private set; } = [];

    public IReadOnlyList<string> Usernames { get; private set; } = [];

    public string? CurrentUsername { get; private set; }

    public string? Message { get; set; }
    public bool MessageIsError { get; set; }

    public void OnGet()
    {
        Load();
    }

    public IActionResult OnPostCreate(string? usuario, string? clave, string? confirmacion,
        UserRole rol = UserRole.Administrativo, bool moduloCambioDomicilio = false, bool moduloF8 = false)
    {
        var result = provisioning.Create(usuario, clave, confirmacion, rol, moduloCambioDomicilio, moduloF8);
        return Finish(result, result == ProvisioningResult.Created
            ? $"Usuario '{usuario?.Trim().ToLowerInvariant()}' creado."
            : UserProvisioning.Describe(result));
    }

    /// <summary>Rol y módulos se editan aparte de la clave — cambian con más frecuencia que una
    /// contraseña, y mezclarlos en el mismo formulario forzaría a tocar la clave para cambiar solo
    /// el rol.</summary>
    public IActionResult OnPostUpdateRole(string usuario, UserRole rol, bool moduloCambioDomicilio = false, bool moduloF8 = false)
    {
        var target = users.FindByUsername(usuario);
        if (target is null)
        {
            return Finish(ProvisioningResult.UserNotFound, UserProvisioning.Describe(ProvisioningResult.UserNotFound));
        }

        // El propio Administrador no puede quitarse a sí mismo el rol: dejaría la app sin nadie
        // que pueda entrar a Usuarios a deshacer el error.
        if (string.Equals(usuario, User.Identity?.Name, StringComparison.OrdinalIgnoreCase) && rol != UserRole.Administrador)
        {
            return Finish(ProvisioningResult.UsernameInvalid, "No puede quitarse a sí mismo el rol de Administrador.");
        }

        users.UpdateRole(target.Id, rol, moduloCambioDomicilio, moduloF8);
        return Finish(ProvisioningResult.Created, $"Rol de '{usuario}' actualizado.");
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
        Users = users.AllUsers();
        Usernames = users.AllUsernames();
        CurrentUsername = User.Identity?.Name;
        Message = TempData["Message"] as string;
        MessageIsError = TempData["MessageIsError"] is true;
    }
}
