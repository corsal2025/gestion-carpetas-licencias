using LicenciasCarpetas.Dashboard.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LicenciasCarpetas.Dashboard.Pages;

/// <summary>
/// First-run screen: creates the first account without a console. It is open to anyone only while
/// the app has no accounts at all — at that point nobody can sign in anyway, so there is nothing to
/// protect. The moment one account exists, this screen closes for good and further accounts are
/// created from /Usuarios, signed in.
/// </summary>
public class SetupModel(UserProvisioning provisioning) : PageModel
{
    [BindProperty]
    public string Username { get; set; } = string.Empty;

    [BindProperty]
    public string Password { get; set; } = string.Empty;

    [BindProperty]
    public string ConfirmPassword { get; set; } = string.Empty;

    public string? ErrorMessage { get; set; }

    public IActionResult OnGet()
        => provisioning.HasNoUsers() ? Page() : RedirectToPage("/Login");

    public IActionResult OnPost()
    {
        if (!provisioning.HasNoUsers())
        {
            return RedirectToPage("/Login");
        }

        var result = provisioning.Create(Username, Password, ConfirmPassword);
        if (result != ProvisioningResult.Created)
        {
            ErrorMessage = UserProvisioning.Describe(result);
            return Page();
        }

        TempData["Message"] = $"Usuario '{Username.Trim().ToLowerInvariant()}' creado. Ya puede ingresar.";
        return RedirectToPage("/Login");
    }
}
