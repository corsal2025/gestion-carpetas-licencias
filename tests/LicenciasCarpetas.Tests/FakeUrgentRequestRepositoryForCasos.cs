using LicenciasCarpetas.F8.Data;
using LicenciasCarpetas.F8.Domain;

namespace LicenciasCarpetas.Tests;

/// <summary>In-memory fake covering Insert/FindByRut/FindById/Update/GetAll/AddFlag/ClearFlags —
/// what Casos' OnPostSave (auto-create an F8 row on "NO EXISTE CARPETA") and F8's own
/// OnPostSetEstado (propagate Estado back to Casos) need. Everything else throws on purpose: if a
/// future change starts calling another member, this fake should fail loudly instead of silently
/// returning a default that hides the gap.</summary>
public sealed class FakeUrgentRequestRepositoryForCasos : IUrgentRequestRepository
{
    private readonly List<UrgentRequest> requests = [];
    private readonly List<(long RequestId, string ColumnName, string ReasonCode, string? RawValue)> flags = [];
    private long nextId = 1;

    public void EnsureSchema() { }

    public long Insert(UrgentRequest request)
    {
        request.Id = nextId++;
        requests.Add(request);
        return request.Id;
    }

    public UrgentRequest? FindByRut(string rut) => requests.FirstOrDefault(r => r.Rut == rut);
    public IReadOnlyList<UrgentRequest> GetAll() => requests;
    public UrgentRequest? FindById(long id) => requests.FirstOrDefault(r => r.Id == id);

    public void Update(UrgentRequest request)
    {
        var existing = FindById(request.Id);
        if (existing is null) return;
        requests.Remove(existing);
        requests.Add(request);
    }

    public void AddFlag(long requestId, string columnName, string reasonCode, string? rawValue) =>
        flags.Add((requestId, columnName, reasonCode, rawValue));
    public IReadOnlyList<ImportFlag> GetFlagsFor(long requestId) => throw new NotImplementedException();
    public IReadOnlyList<UrgentRequest> GetFlagged() => throw new NotImplementedException();
    public void ClearFlags(long requestId) => flags.RemoveAll(f => f.RequestId == requestId);

    public void Delete(long id) => throw new NotImplementedException();
    public IReadOnlyList<UrgentRequest> Query(UrgentRequestFilter filter, string? search) => throw new NotImplementedException();
    public bool HasCompletedImport(string sourceFile) => throw new NotImplementedException();
    public void RecordImportRun(string sourceFile, int rowsImported, int rowsFlagged) => throw new NotImplementedException();
    public void DeleteImportedRows() => throw new NotImplementedException();
    public void SetMarked(long id, bool marked) => throw new NotImplementedException();
    public void SetPendienteCarpeta(long id, bool pendienteCarpeta) => throw new NotImplementedException();
    public void SetSectorPdfGenerated(long id, DateTimeOffset generatedAt) => throw new NotImplementedException();
    public void SetImpresoMensual(long id, DateTimeOffset? generatedAt) => throw new NotImplementedException();
    public void SetPendienteEscrituraExcel(long id, bool pendiente) => throw new NotImplementedException();
    public IReadOnlyList<UrgentRequest> GetPendingEscrituraExcel() => throw new NotImplementedException();
}
