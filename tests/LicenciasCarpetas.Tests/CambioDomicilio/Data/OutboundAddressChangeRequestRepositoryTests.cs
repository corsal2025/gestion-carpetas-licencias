using Microsoft.Data.Sqlite;
using LicenciasCarpetas.CambioDomicilio.Data;
using LicenciasCarpetas.CambioDomicilio.Domain;

namespace LicenciasCarpetas.Tests.CambioDomicilio.Data;

public class OutboundAddressChangeRequestRepositoryTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"licencias-carpetas-oacd-{Guid.NewGuid():N}.db");
    private readonly string _connectionString;
    private readonly OutboundAddressChangeRequestRepository _repository;

    public OutboundAddressChangeRequestRepositoryTests()
    {
        _connectionString = $"Data Source={_path}";
        _repository = new OutboundAddressChangeRequestRepository(_connectionString);
        _repository.EnsureSchema();
    }

    private static OutboundAddressChangeRequest Request(
        string fullName = "JUAN PEREZ",
        string rut = "13.025.150-1",
        string destinationComuna = "VIÑA DEL MAR",
        OutboundRequestStatus status = OutboundRequestStatus.Borrador)
        => new()
        {
            FullName = fullName,
            Rut = rut,
            Phone = "+56912345678",
            Street = "Calle Falsa",
            Number = "123",
            Unit = "Depto 4B",
            DestinationComuna = destinationComuna,
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = 1
        };

    [Fact]
    public void Inserts_and_reads_back_a_request_with_all_fields()
    {
        var id = _repository.Insert(Request());
        var stored = _repository.FindById(id);

        Assert.NotNull(stored);
        Assert.Equal("JUAN PEREZ", stored.FullName);
        Assert.Equal("13.025.150-1", stored.Rut);
        Assert.Equal("+56912345678", stored.Phone);
        Assert.Equal("Calle Falsa", stored.Street);
        Assert.Equal("123", stored.Number);
        Assert.Equal("Depto 4B", stored.Unit);
        Assert.Equal("VIÑA DEL MAR", stored.DestinationComuna);
        Assert.Equal(OutboundRequestStatus.Borrador, stored.Status);
        Assert.Equal(1, stored.CreatedByUserId);
        Assert.Null(stored.SentAt);
        Assert.Null(stored.SentByUserId);
    }

    [Fact]
    public void Insert_always_forces_Status_to_Borrador()
    {
        var id = _repository.Insert(Request(status: OutboundRequestStatus.Enviada));
        var stored = _repository.FindById(id);

        Assert.Equal(OutboundRequestStatus.Borrador, stored!.Status);
    }

    [Fact]
    public void GetAll_returns_newest_first()
    {
        var firstId = _repository.Insert(Request(fullName: "PRIMERO"));
        var secondId = _repository.Insert(Request(fullName: "SEGUNDO"));
        var thirdId = _repository.Insert(Request(fullName: "TERCERO"));

        var all = _repository.GetAll();

        Assert.Equal([thirdId, secondId, firstId], all.Select(r => r.Id));
    }

    [Fact]
    public void MarkSent_transitions_Borrador_to_Enviada_and_is_a_noop_the_second_time()
    {
        var id = _repository.Insert(Request());
        var sentAt = DateTimeOffset.UtcNow;

        _repository.MarkSent(id, sentAt, sentByUserId: 7);

        var stored = _repository.FindById(id);
        Assert.Equal(OutboundRequestStatus.Enviada, stored!.Status);
        Assert.Equal(sentAt.ToString("O"), stored.SentAt!.Value.ToString("O"));
        Assert.Equal(7, stored.SentByUserId);

        var laterSentAt = sentAt.AddHours(1);
        _repository.MarkSent(id, laterSentAt, sentByUserId: 99);

        var stillStored = _repository.FindById(id);
        Assert.Equal(sentAt.ToString("O"), stillStored!.SentAt!.Value.ToString("O"));
        Assert.Equal(7, stillStored.SentByUserId);
    }

    [Fact]
    public void AddAttachment_and_GetAttachments_round_trip_multiple_attachments_in_order()
    {
        var requestId = _repository.Insert(Request());
        var firstUploadedAt = DateTimeOffset.UtcNow;

        var firstId = _repository.AddAttachment(new OutboundAddressChangeAttachment
        {
            RequestId = requestId,
            FileName = "carnet.pdf",
            StoredPath = "/uploads/carnet.pdf",
            ContentType = "application/pdf",
            UploadedAt = firstUploadedAt
        });
        var secondId = _repository.AddAttachment(new OutboundAddressChangeAttachment
        {
            RequestId = requestId,
            FileName = "comprobante.pdf",
            StoredPath = "/uploads/comprobante.pdf",
            ContentType = "application/pdf"
        });

        var attachments = _repository.GetAttachments(requestId);

        Assert.Equal([firstId, secondId], attachments.Select(a => a.Id));
        Assert.Equal("carnet.pdf", attachments[0].FileName);
        Assert.Equal("/uploads/carnet.pdf", attachments[0].StoredPath);
        Assert.Equal("application/pdf", attachments[0].ContentType);
        Assert.Equal(firstUploadedAt.ToString("O"), attachments[0].UploadedAt.ToString("O"));
        Assert.Equal("comprobante.pdf", attachments[1].FileName);
    }

    [Fact]
    public void DeleteAttachment_removes_it_from_GetAttachments()
    {
        var requestId = _repository.Insert(Request());
        var attachmentId = _repository.AddAttachment(new OutboundAddressChangeAttachment
        {
            RequestId = requestId,
            FileName = "carnet.pdf",
            StoredPath = "/uploads/carnet.pdf",
            ContentType = "application/pdf"
        });

        _repository.DeleteAttachment(attachmentId);

        Assert.Empty(_repository.GetAttachments(requestId));
    }

    /// <summary>Reproduces a database created before this fix: Street/Number NOT NULL, no
    /// SourceFolderCaseId column — exactly what CREATE TABLE IF NOT EXISTS alone cannot fix on an
    /// already-existing table. EnsureSchema() must rebuild it so a row with both blank can be
    /// inserted and read back, not just leave the old NOT NULL constraint in place.</summary>
    [Fact]
    public void EnsureSchema_migrates_a_pre_existing_table_with_NOT_NULL_Street_and_Number()
    {
        var path = Path.Combine(Path.GetTempPath(), $"licencias-carpetas-oacd-legacy-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={path}";
        try
        {
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE OutboundAddressChangeRequest (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        FullName TEXT NOT NULL,
                        Rut TEXT NOT NULL,
                        Phone TEXT NULL,
                        Street TEXT NOT NULL,
                        Number TEXT NOT NULL,
                        Unit TEXT NULL,
                        DestinationComuna TEXT NOT NULL,
                        Status TEXT NOT NULL,
                        CreatedAt TEXT NOT NULL,
                        SentAt TEXT NULL,
                        SentByUserId INTEGER NULL,
                        CreatedByUserId INTEGER NOT NULL
                    );
                    """;
                command.ExecuteNonQuery();
            }

            var repository = new OutboundAddressChangeRequestRepository(connectionString);
            repository.EnsureSchema();

            var id = repository.Insert(new OutboundAddressChangeRequest
            {
                FullName = "MARIA SOTO",
                Rut = "9.879.451-3",
                Street = null,
                Number = null,
                DestinationComuna = "QUILPUÉ",
                CreatedByUserId = 1
            });

            var stored = repository.FindById(id);
            Assert.NotNull(stored);
            Assert.Null(stored.Street);
            Assert.Null(stored.Number);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
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
