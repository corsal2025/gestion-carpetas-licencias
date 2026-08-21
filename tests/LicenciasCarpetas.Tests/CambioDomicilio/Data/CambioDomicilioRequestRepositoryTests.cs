using LicenciasCarpetas.CambioDomicilio.Data;
using LicenciasCarpetas.CambioDomicilio.Domain;
using LicenciasCarpetas.F8.Data;
using LicenciasCarpetas.Persistence;

namespace LicenciasCarpetas.Tests.CambioDomicilio.Data;

public class CambioDomicilioRequestRepositoryTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"licencias-carpetas-cd-{Guid.NewGuid():N}.db");
    private readonly string _connectionString;
    private readonly CambioDomicilioRequestRepository _repository;

    public CambioDomicilioRequestRepositoryTests()
    {
        _connectionString = $"Data Source={_path}";
        _repository = new CambioDomicilioRequestRepository(_connectionString);
        _repository.EnsureSchema();
    }

    private static PersonRequest Request(
        string sourceMessageId = "msg-1",
        string rut = "13.025.150-1",
        string comuna = "VIÑA DEL MAR",
        RequestStatus status = RequestStatus.Pending)
        => new()
        {
            FullName = "JUAN PEREZ",
            Rut = rut,
            Comuna = comuna,
            SourceMessageId = sourceMessageId,
            SourceSubject = "Solicitud de carpeta",
            SourceSender = "licencias@munivina.cl",
            Status = status,
            ReceivedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };

    [Fact]
    public void Inserts_and_reads_back_a_request()
    {
        var id = _repository.Insert(Request());
        var stored = _repository.FindById(id);

        Assert.NotNull(stored);
        Assert.Equal("JUAN PEREZ", stored.FullName);
        Assert.Equal(RequestStatus.Pending, stored.Status);
        Assert.Equal(CaseDestination.None, stored.Destination);
    }

    [Fact]
    public void MarkUploaded_only_changes_a_pending_request()
    {
        var id = _repository.Insert(Request());
        var uploadedAt = DateTimeOffset.UtcNow;

        _repository.MarkUploaded(id, uploadedAt);

        var stored = _repository.FindById(id);
        Assert.Equal(RequestStatus.Uploaded, stored!.Status);
        Assert.Equal(uploadedAt.ToString("O"), stored.UploadedAt!.Value.ToString("O"));
    }

    [Fact]
    public void SetFechaUltimaCarpeta_and_SetSinCarpeta_are_mutually_exclusive()
    {
        var id = _repository.Insert(Request());

        _repository.SetFechaUltimaCarpeta(id, new DateOnly(2026, 1, 15));
        Assert.False(_repository.FindById(id)!.SinCarpeta);

        _repository.SetSinCarpeta(id);
        var stored = _repository.FindById(id);
        Assert.True(stored!.SinCarpeta);
        Assert.Null(stored.FechaUltimaCarpeta);
    }

    [Fact]
    public void SetDestination_and_ClearDestination_round_trip()
    {
        var id = _repository.Insert(Request());
        var transferredAt = DateTimeOffset.UtcNow;

        _repository.SetDestination(id, CaseDestination.F8, transferredAt);
        Assert.Equal(CaseDestination.F8, _repository.FindById(id)!.Destination);

        _repository.ClearDestination(id);
        var stored = _repository.FindById(id);
        Assert.Equal(CaseDestination.None, stored!.Destination);
        Assert.Null(stored.TransferredAt);
    }

    [Fact]
    public void RecordDeletedSourceMessage_tombstones_the_message_id()
    {
        _repository.RecordDeletedSourceMessage("msg-deleted");

        Assert.True(_repository.IsSourceMessageDeleted("msg-deleted"));
        Assert.False(_repository.IsSourceMessageDeleted("msg-other"));
    }

    [Fact]
    public void SetPenultimasCarpetasPdfGenerated_round_trips()
    {
        var id = _repository.Insert(Request());
        var generatedAt = DateTimeOffset.UtcNow;

        _repository.SetPenultimasCarpetasPdfGenerated(id, generatedAt);

        Assert.Equal(generatedAt.ToString("O"), _repository.FindById(id)!.PenultimasCarpetasPdfGeneratedAt!.Value.ToString("O"));
    }

    /// <summary>PersonRequest lives in the same carpetas.db as FolderCase (Casos) and UrgentRequest
    /// (F8) — this is the whole point of folding the module in rather than linking to a separate
    /// app. A schema clash here would silently corrupt every other module's data.</summary>
    [Fact]
    public void Coexists_in_the_same_database_file_as_FolderCase_and_UrgentRequest()
    {
        var folderCases = new FolderCaseRepository(_connectionString);
        var urgentRequests = new UrgentRequestRepository(_connectionString);
        folderCases.EnsureSchema();
        urgentRequests.EnsureSchema();

        var id = _repository.Insert(Request());

        Assert.NotNull(_repository.FindById(id));
        Assert.Empty(folderCases.QueryAll(new CaseFilter()));
        Assert.Empty(urgentRequests.GetAll());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}
