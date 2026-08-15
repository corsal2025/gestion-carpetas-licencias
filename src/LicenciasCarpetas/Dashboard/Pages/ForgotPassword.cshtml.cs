using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LicenciasCarpetas.Dashboard.Pages;

/// <summary>
/// Recovery is deliberately an operation run on the machine itself, not an email link: this app has
/// no mail transport, and inventing a "we sent you an email" that never arrives would be worse than
/// telling the operator exactly what to do.
/// </summary>
public class ForgotPasswordModel : PageModel
{
    public void OnGet()
    {
    }
}
