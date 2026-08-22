using LicenciasCarpetas.F8.Data;
using LicenciasCarpetas.F8.Domain;

namespace LicenciasCarpetas.Tests.F8;

/// <summary>In-memory fake covering only GetAll() — the one method InicioModel actually calls.
/// Everything else throws on purpose: if a future change starts calling another member, this fake
/// should fail loudly instead of silently returning a default that hides the gap.</summary>
public sealed class FakeUrgentRequestRepository : IUrgentRequestRepository
{
    private readonly List<UrgentRequest> requests = [];

    public void Seed(UrgentRequest request) => requests.Add(request);

    public IReadOnlyList<UrgentRequest> GetAll() => requests;

    public void EnsureSchema() => throw new NotImplementedException();
    public long Insert(UrgentRequest request) => throw new NotImplementedException();
    public UrgentRequest? FindById(long id) => throw new NotImplementedException();
    public void Update(UrgentRequest request) => throw new NotImplementedException();
    public void Delete(long id) => throw new NotImplementedException();
    public IReadOnlyList<UrgentRequest> Query(UrgentRequestFilter filter, string? search) => throw new NotImplementedException();
    public void AddFlag(long requestId, string columnName, string reasonCode, string? rawValue) => throw new NotImplementedException();
    public IReadOnlyList<ImportFlag> GetFlagsFor(long requestId) => throw new NotImplementedException();
    public IReadOnlyList<UrgentRequest> GetFlagged() => throw new NotImplementedException();
    public void ClearFlags(long requestId) => throw new NotImplementedException();
    public bool HasCompletedImport(string sourceFile) => throw new NotImplementedException();
    public void RecordImportRun(string sourceFile, int rowsImported, int rowsFlagged) => throw new NotImplementedException();
    public void DeleteImportedRows() => throw new NotImplementedException();
    public void SetMarked(long id, bool marked) => throw new NotImplementedException();
    public void SetPendienteCarpeta(long id, bool pendienteCarpeta) => throw new NotImplementedException();
    public void SetSectorPdfGenerated(long id, DateTimeOffset generatedAt) => throw new NotImplementedException();
    public void SetImpresoMensual(long id, DateTimeOffset? generatedAt) => throw new NotImplementedException();
    public UrgentRequest? FindByRut(string rut) => throw new NotImplementedException();
    public void SetPendienteEscrituraExcel(long id, bool pendiente) => throw new NotImplementedException();
    public IReadOnlyList<UrgentRequest> GetPendingEscrituraExcel() => throw new NotImplementedException();
}
