using Microsoft.AspNetCore.Authorization;
using LicenciasCarpetas.F8.Data;
using LicenciasCarpetas.F8.Domain;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LicenciasCarpetas.Dashboard.Pages.F8;

[Authorize(Policy = "F8Access")]
public sealed class EstadisticasModel(IUrgentRequestRepository repository) : PageModel
{
    private const string EstadoActualSubida = "SUBIDA A CONASET";

    public int Total { get; private set; }
    public int Subidas { get; private set; }
    public int Pendientes { get; private set; }
    public int NeedsReview { get; private set; }
    public int Marcados { get; private set; }
    public int PendienteCarpeta { get; private set; }
    public IReadOnlyList<(string Label, int Count)> PorEstado { get; private set; } = [];
    public IReadOnlyList<(string Label, int Count)> PorEstadoActual { get; private set; } = [];
    public IReadOnlyList<(string Label, int Count)> PorSector { get; private set; } = [];
    public IReadOnlyList<(string Label, int Count)> PorOrigen { get; private set; } = [];
    public IReadOnlyList<(string WeekLabel, int Count)> IngresosPorSemana { get; private set; } = [];
    public double? TiempoPromedioConfirmacionDias { get; private set; }

    public void OnGet()
    {
        var all = repository.GetAll();

        Total = all.Count;
        Subidas = all.Count(r => r.EstadoActual == EstadoActualSubida);
        Pendientes = Total - Subidas;
        NeedsReview = all.Count(r => r.NeedsReview);
        Marcados = all.Count(r => r.Marked);
        PendienteCarpeta = all.Count(r => r.PendienteCarpeta);

        PorEstado = CountBy(all, r => string.IsNullOrWhiteSpace(r.Estado) ? "(sin estado)" : r.Estado!);

        PorEstadoActual = CountBy(all, r =>
            string.IsNullOrWhiteSpace(r.EstadoActual) ? "PENDIENTE" : r.EstadoActual!);

        PorSector = CountBy(all, r => r.Sector switch
        {
            FolderSector.Archivo => "Archivo",
            FolderSector.Oficina43 => "Oficina 43",
            _ => "(sin fecha ultima carpeta)",
        });

        PorOrigen = CountBy(all, r => r.Origin switch
        {
            "Matriz" => "Matriz (automatico)",
            "Import" => "Importado (Excel historico)",
            "Web" => "Manual (dashboard)",
            var other => other,
        });

        IngresosPorSemana = all
            .Where(r => r.FechaPeticion is not null)
            .GroupBy(r => StartOfWeek(r.FechaPeticion!.Value))
            .OrderBy(g => g.Key)
            .Select(g => (WeekLabel: g.Key.ToString("dd/MM"), Count: g.Count()))
            .ToList();

        var confirmados = all
            .Where(r => r.FechaPeticion is not null && r.FechaDeSubida is not null && r.FechaDeSubida >= r.FechaPeticion)
            .Select(r => r.FechaDeSubida!.Value.DayNumber - r.FechaPeticion!.Value.DayNumber)
            .ToList();
        TiempoPromedioConfirmacionDias = confirmados.Count > 0 ? confirmados.Average() : null;
    }

    // Monday-start ISO week bucket for the "ingresos por semana" trend.
    private static DateOnly StartOfWeek(DateOnly date)
    {
        var diff = ((int)date.DayOfWeek == 0 ? 7 : (int)date.DayOfWeek) - 1;
        return date.AddDays(-diff);
    }

    private static IReadOnlyList<(string Label, int Count)> CountBy(
        IReadOnlyList<UrgentRequest> requests, Func<UrgentRequest, string> selector) =>
        requests
            .GroupBy(selector)
            .Select(g => (Label: g.Key, Count: g.Count()))
            .OrderByDescending(x => x.Count)
            .ToList();
}
