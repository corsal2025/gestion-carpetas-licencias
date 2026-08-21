using Microsoft.AspNetCore.Authorization;
using LicenciasCarpetas.F8.Data;
using LicenciasCarpetas.F8.Domain;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LicenciasCarpetas.Dashboard.Pages.F8;

[Authorize(Policy = "F8Access")]
public sealed class ReviewModel(IUrgentRequestRepository repository) : PageModel
{
    public sealed record FlaggedRow(UrgentRequest Request, IReadOnlyList<ImportFlag> Flags);

    public IReadOnlyList<FlaggedRow> FlaggedRequests { get; private set; } = [];

    public void OnGet()
    {
        FlaggedRequests = repository.GetFlagged()
            .Select(r => new FlaggedRow(r, repository.GetFlagsFor(r.Id)))
            .ToList();
    }
}
