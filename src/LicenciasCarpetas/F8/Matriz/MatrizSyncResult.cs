namespace LicenciasCarpetas.F8.Matriz;

public sealed class MatrizSyncResult
{
    public int CasesCreated { get; set; }
    public int RowsWrittenBack { get; set; }
    public bool ExcelLocked { get; set; }
    public bool NoOp { get; set; }
    public List<string> Alerts { get; } = [];

    public string Summary() =>
        NoOp
            ? "No habia archivo de matriz configurado."
            : ExcelLocked
                ? "El Excel matriz estaba abierto/bloqueado — no se pudo sincronizar, reintenta en un momento."
                : $"Sincronizacion completa: {CasesCreated} caso(s) nuevo(s) importado(s), {RowsWrittenBack} fila(s) actualizada(s) en el Excel." +
                  (Alerts.Count > 0 ? $" {Alerts.Count} alerta(s) para revisar." : string.Empty);
}
