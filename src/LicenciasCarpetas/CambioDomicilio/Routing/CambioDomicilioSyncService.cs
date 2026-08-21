using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using LicenciasCarpetas.CambioDomicilio;
using LicenciasCarpetas.CambioDomicilio.Ews;
using LicenciasCarpetas.CambioDomicilio.Data;
using LicenciasCarpetas.CambioDomicilio.Reporting;

namespace LicenciasCarpetas.CambioDomicilio.Routing;

/// <summary>What one poll cycle actually did — distinguishes "nothing ran because a previous cycle
/// was still in flight" from "the cycle ran but blew up partway through", so the operator's
/// "Sincronizar ahora" button can report a real failure instead of a false "completada".</summary>
public enum CambioDomicilioSyncOutcome
{
    Completed,
    SkippedBusy,
    Failed,
}

public sealed class CambioDomicilioSyncService(
    AddressChangeRoutingService routingService,
    IEmailReader emailReader,
    ICambioDomicilioRequestRepository repository,
    ICsvReportWriter reportWriter,
    CambioDomicilioOptions options,
    ILogger<CambioDomicilioSyncService> logger) : BackgroundService
{
    private readonly SemaphoreSlim cycleGuard = new(1, 1);

    /// <summary>No automatic polling — sync only runs when the operator presses "Sincronizar
    /// ahora" on the dashboard (see IndexModel.OnPostSyncNowAsync), which calls RunCycleAsync
    /// directly. This method just ensures the schema exists at startup.</summary>
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        repository.EnsureSchema();
        return Task.CompletedTask;
    }

    /// <summary>Runs one poll cycle (used by the dashboard's manual "sync now" action to report
    /// accurate feedback — see <see cref="CambioDomicilioSyncOutcome"/>).</summary>
    internal async Task<CambioDomicilioSyncOutcome> RunCycleAsync(CancellationToken cancellationToken)
    {
        if (!await cycleGuard.WaitAsync(TimeSpan.Zero, cancellationToken))
        {
            logger.LogWarning("Ciclo anterior aún en ejecución, se omite este tick");
            return CambioDomicilioSyncOutcome.SkippedBusy;
        }

        try
        {
            var contacts = routingService.LoadDirectory();
            if (contacts.Count == 0)
            {
                logger.LogCritical(
                    "El directorio de comunas ({CsvPath}) está vacío o no se pudo leer. Se omite este ciclo completo para no perder correos silenciosamente",
                    options.ComunaDirectoryCsvPath);
                return CambioDomicilioSyncOutcome.Completed;
            }

            var incoming = await emailReader.GetMessagesInFolderAsync(options.SourceFolderName, cancellationToken);
            foreach (var email in incoming)
            {
                try
                {
                    routingService.ProcessIncomingRequest(email, contacts);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error procesando un correo de '{Folder}', se continúa con el resto del lote", options.SourceFolderName);
                }
            }

            var confirmations = await emailReader.GetMessagesInFolderAsync(options.ConfirmationFolderName, cancellationToken);
            foreach (var email in confirmations)
            {
                try
                {
                    routingService.ProcessUploadedCase(email);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error procesando un correo de '{Folder}', se continúa con el resto del lote", options.ConfirmationFolderName);
                }
            }

            if (!string.IsNullOrWhiteSpace(options.ReportCsvPath))
            {
                reportWriter.Write(repository.GetAll(), options.ReportCsvPath);
            }
            logger.LogInformation(
                "Ciclo completado: {IncomingCount} en '{SourceFolder}', {ConfirmationCount} en '{ConfirmationFolder}'",
                incoming.Count, options.SourceFolderName, confirmations.Count, options.ConfirmationFolderName);
            return CambioDomicilioSyncOutcome.Completed;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Fallo el ciclo de sondeo, se reintentará en el próximo tick");
            return CambioDomicilioSyncOutcome.Failed;
        }
        finally
        {
            cycleGuard.Release();
        }
    }
}
