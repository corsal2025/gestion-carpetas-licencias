using ClosedXML.Excel;
using LicenciasCarpetas.CambioDomicilio.Data;
using LicenciasCarpetas.CambioDomicilio.Domain;
using LicenciasCarpetas.CambioDomicilio.Extraction;

namespace LicenciasCarpetas.CambioDomicilio.Solicitar;

public sealed class SolicitarSyncResult
{
    /// <summary>No hay archivo configurado (CambioDomicilio:SolicitarMatrizExcelPath vacío) o no
    /// existe en disco — mismo caso que F8:MatrizExcelPath sin configurar.</summary>
    public bool NoOp { get; set; }
    public int Created { get; set; }
    public List<string> Alerts { get; } = [];

    public string Summary() => Created == 1
        ? "1 solicitud nueva cargada."
        : $"{Created} solicitudes nuevas cargadas.";
}

/// <summary>Botón "Sincronizar Ahora" de Solicitar Cambios de Domicilio — mismo mecanismo que
/// F8's "Sincronizar ahora" (MatrizSyncService): lee un Excel configurado por ruta en vez de un
/// buzón de correo, en vez de exigir que el operador cargue cada solicitud a mano. Solo lectura
/// (no hay "salida"/escritura de vuelta al Excel, a diferencia de F8) — cada fila nueva se crea
/// como Borrador, exactamente como si se hubiera cargado por "+ Nueva Solicitud" antes.</summary>
public static class SolicitarMatrizSyncService
{
    // Dos clics casi simultáneos (doble click, o dos operadores) podían leer el mismo RUT como
    // "todavía no existe" antes de que el primero terminara de insertarlo, y los dos lo cargaban
    // — el mismo problema que CambioDomicilioSyncService ya resuelve con su propio cycleGuard.
    // Un solo semáforo estático alcanza: la app corre en un único proceso Kestrel.
    private static readonly SemaphoreSlim SyncGuard = new(1, 1);

    public static SolicitarSyncResult Sync(string? workbookPath, IOutboundAddressChangeRequestRepository repository, long userId)
    {
        var result = new SolicitarSyncResult();
        if (string.IsNullOrWhiteSpace(workbookPath) || !File.Exists(workbookPath))
        {
            result.NoOp = true;
            return result;
        }

        if (!SyncGuard.Wait(TimeSpan.Zero))
        {
            result.Alerts.Add("Ya hay una sincronización en curso — espere a que termine e intente de nuevo.");
            return result;
        }

        try
        {
            return RunSync(workbookPath, repository, userId, result);
        }
        finally
        {
            SyncGuard.Release();
        }
    }

    private static SolicitarSyncResult RunSync(string workbookPath, IOutboundAddressChangeRequestRepository repository, long userId, SolicitarSyncResult result)
    {
        // Compartido con Excel/Drive abierto en paralelo, igual que el importador principal.
        using var stream = new FileStream(workbookPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheets.First();

        var headerRow = sheet.FirstRowUsed();
        if (headerRow is null)
        {
            return result;
        }

        int? rutCol = null, nombreCol = null, comunaCol = null;
        foreach (var cell in headerRow.CellsUsed())
        {
            var header = cell.GetString().Trim().ToUpperInvariant();
            if (header == "RUT")
            {
                rutCol = cell.Address.ColumnNumber;
            }
            else if (header is "NOMBRE" or "NOMBRE COMPLETO")
            {
                nombreCol = cell.Address.ColumnNumber;
            }
            else if (header == "COMUNA")
            {
                comunaCol = cell.Address.ColumnNumber;
            }
        }

        if (rutCol is null || nombreCol is null || comunaCol is null)
        {
            result.Alerts.Add("No se reconocieron las columnas RUT/NOMBRE/COMUNA en la primera hoja del Excel.");
            return result;
        }

        // Un mismo RUT no debe cargarse dos veces cada vez que se sincroniza — el archivo no se
        // vacía ni se marca fila por fila entre corridas.
        var existingRuts = repository.GetAll()
            .Select(r => RutValidator.NormalizeAndValidate(r.Rut))
            .Where(rut => rut is not null)
            .ToHashSet();

        foreach (var row in sheet.RowsUsed().Skip(headerRow.RowNumber()))
        {
            var rutRaw = row.Cell(rutCol.Value).GetString().Trim();
            var nombre = row.Cell(nombreCol.Value).GetString().Trim();
            var comuna = row.Cell(comunaCol.Value).GetString().Trim();

            if (rutRaw.Length == 0 && nombre.Length == 0 && comuna.Length == 0)
            {
                continue;
            }

            var rut = RutValidator.NormalizeAndValidate(rutRaw);
            if (rut is null)
            {
                result.Alerts.Add($"RUT inválido en fila {row.RowNumber()}: '{rutRaw}' — no se cargó.");
                continue;
            }

            if (nombre.Length == 0 || comuna.Length == 0)
            {
                result.Alerts.Add($"Fila {row.RowNumber()} (RUT {rut}) sin nombre o sin comuna — no se cargó.");
                continue;
            }

            if (!existingRuts.Add(rut))
            {
                continue;
            }

            repository.Insert(new OutboundAddressChangeRequest
            {
                FullName = nombre,
                Rut = rut,
                DestinationComuna = comuna,
                CreatedByUserId = userId
            });
            result.Created++;
        }

        return result;
    }
}
