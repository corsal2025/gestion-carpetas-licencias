using LicenciasCarpetas.F8.Domain;

namespace LicenciasCarpetas.F8.Data;

public sealed record UrgentRequestFilter(string? Month, string? Estado, string? EstadoActual, bool? Flagged);

public interface IUrgentRequestRepository
{
    void EnsureSchema();

    long Insert(UrgentRequest request);
    UrgentRequest? FindById(long id);
    void Update(UrgentRequest request);
    void Delete(long id);
    IReadOnlyList<UrgentRequest> GetAll();
    IReadOnlyList<UrgentRequest> Query(UrgentRequestFilter filter, string? search);

    void AddFlag(long requestId, string columnName, string reasonCode, string? rawValue);
    IReadOnlyList<ImportFlag> GetFlagsFor(long requestId);
    IReadOnlyList<UrgentRequest> GetFlagged();
    void ClearFlags(long requestId);

    bool HasCompletedImport(string sourceFile);
    void RecordImportRun(string sourceFile, int rowsImported, int rowsFlagged);
    void DeleteImportedRows();

    void SetMarked(long id, bool marked);
    void SetPendienteCarpeta(long id, bool pendienteCarpeta);
    void SetSectorPdfGenerated(long id, DateTimeOffset generatedAt);
    void SetImpresoMensual(long id, DateTimeOffset? generatedAt);

    UrgentRequest? FindByRut(string rut);
    void SetPendienteEscrituraExcel(long id, bool pendiente);
    IReadOnlyList<UrgentRequest> GetPendingEscrituraExcel();
}
