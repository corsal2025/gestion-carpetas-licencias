using LicenciasCarpetas.Domain;
using LicenciasCarpetas.Persistence;
using LicenciasCarpetas.Statistics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LicenciasCarpetas.Dashboard.Pages;

[Authorize]
public class EstadisticasModel(
    StatisticsService statistics,
    IDailyCounterRepository counters,
    IFolderCaseRepository cases) : PageModel
{
    public MonthlyStatistics? Statistics { get; private set; }
    public IReadOnlyList<int> Years { get; private set; } = [];

    [BindProperty(SupportsGet = true)]
    public int Year { get; set; } = DateTime.Today.Year;

    [BindProperty(SupportsGet = true)]
    public int Month { get; set; } = DateTime.Today.Month;

    public string? Message { get; set; }

    public void OnGet()
    {
        Load();
    }

    /// <summary>Escaneadas / subidas are the only numbers nobody can derive from the agenda, so they
    /// stay hand-entered exactly like in the workbook.</summary>
    public IActionResult OnPostSetCounters(string fecha, int? escaneadas, int? subidas)
    {
        if (!DateOnly.TryParseExact(fecha, "yyyy-MM-dd", out var date))
        {
            TempData["Message"] = $"Fecha inválida: {fecha}";
            return RedirectToPage(new { year = Year, month = Month });
        }

        counters.Upsert(new DailyCounter { Date = date, Scanned = escaneadas, Uploaded = subidas });
        TempData["Message"] = $"Contadores del {date:dd-MM-yyyy} guardados.";
        return RedirectToPage(new { year = Year, month = Month });
    }

    private void Load()
    {
        Years = cases.DistinctYears();
        if (Years.Count > 0 && !Years.Contains(Year))
        {
            Year = Years[0];
        }

        Statistics = statistics.ForMonth(Year, Month);
        Message = TempData["Message"] as string;
    }

    public static string Percent(double? rate) => rate is { } value ? $"{value * 100:0.#}%" : "—";
}
