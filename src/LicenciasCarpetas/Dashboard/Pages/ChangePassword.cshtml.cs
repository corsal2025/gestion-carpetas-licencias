using System.Security.Claims;
using LicenciasCarpetas.Dashboard.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LicenciasCarpetas.Dashboard.Pages;

[Authorize]
public class ChangePasswordModel(IUserRepository users) : PageModel
{
    [BindProperty]
    public string CurrentPassword { get; set; } = string.Empty;

    [BindProperty]
    public string NewPassword { get; set; } = string.Empty;

    [BindProperty]
    public string ConfirmPassword { get; set; } = string.Empty;

    public string? Message { get; set; }
    public bool MessageIsError { get; set; }

    public void OnGet()
    {
    }

    public IActionResult OnPost()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!long.TryParse(userId, out var id) || users.FindById(id) is not { } user)
        {
            return RedirectToPage("/Login");
        }

        if (!PasswordHasher.Verify(CurrentPassword, user.PasswordHash, user.PasswordSalt, user.Iterations))
        {
            Message = "La contraseña actual no es correcta.";
            MessageIsError = true;
            return Page();
        }

        if (NewPassword.Length < 8)
        {
            Message = "La nueva contraseña debe tener al menos 8 caracteres.";
            MessageIsError = true;
            return Page();
        }

        if (NewPassword != ConfirmPassword)
        {
            Message = "La confirmación no coincide con la nueva contraseña.";
            MessageIsError = true;
            return Page();
        }

        var (hash, salt, iterations) = PasswordHasher.Hash(NewPassword);
        users.UpdatePassword(user.Id, hash, salt, iterations);

        Message = "Contraseña actualizada.";
        return Page();
    }
}
