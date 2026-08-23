using Microsoft.Data.Sqlite;
using LicenciasCarpetas.CambioDomicilio.Domain;
using LicenciasCarpetas.Domain;

namespace LicenciasCarpetas.CambioDomicilio.Data;

public interface IOutboundAddressChangeRequestRepository
{
    void EnsureSchema();
    long Insert(OutboundAddressChangeRequest request);
    void Update(OutboundAddressChangeRequest request);
    OutboundAddressChangeRequest? FindById(long id);
    IReadOnlyList<OutboundAddressChangeRequest> GetAll();
    IReadOnlyList<OutboundAddressChangeRequest> FindBySourceFolderCaseId(long folderCaseId);
    bool MarkSent(long id, DateTimeOffset sentAt, long sentByUserId);
    void Delete(long id);

    long AddAttachment(OutboundAddressChangeAttachment attachment);
    IReadOnlyList<OutboundAddressChangeAttachment> GetAttachments(long requestId);
    void DeleteAttachment(long attachmentId);
}

public sealed class OutboundAddressChangeRequestRepository(string connectionString) : IOutboundAddressChangeRequestRepository
{
    public void EnsureSchema()
    {
        using var connection = Open();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS OutboundAddressChangeRequest (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    FullName TEXT NOT NULL,
                    Rut TEXT NOT NULL,
                    Phone TEXT NULL,
                    Street TEXT NULL,
                    Number TEXT NULL,
                    Unit TEXT NULL,
                    DestinationComuna TEXT NOT NULL,
                    Status TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    SentAt TEXT NULL,
                    SentByUserId INTEGER NULL,
                    CreatedByUserId INTEGER NOT NULL,
                    SourceFolderCaseId INTEGER NULL,
                    WorkflowState TEXT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_OutboundAddressChangeRequest_Status ON OutboundAddressChangeRequest (Status);

                CREATE TABLE IF NOT EXISTS OutboundAddressChangeAttachment (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    RequestId INTEGER NOT NULL REFERENCES OutboundAddressChangeRequest(Id),
                    FileName TEXT NOT NULL,
                    StoredPath TEXT NOT NULL,
                    ContentType TEXT NOT NULL,
                    UploadedAt TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_OutboundAddressChangeAttachment_RequestId ON OutboundAddressChangeAttachment (RequestId);
                """;
            command.ExecuteNonQuery();
        }

        // CREATE TABLE IF NOT EXISTS above is a no-op against a database that already has this
        // table from before this fix, which had Street/Number as NOT NULL and no
        // SourceFolderCaseId column. SQLite has no ALTER COLUMN, so a NOT NULL → NULL change needs
        // the standard rebuild-and-swap; a missing nullable column, on its own, can just be added.
        MigrateLegacySchema(connection);
    }

    /// <summary>Brings an existing (pre-fix) OutboundAddressChangeRequest table up to the current
    /// shape. Safe to call on every startup: a database created fresh by the CREATE TABLE above,
    /// or one already migrated, matches on both checks and this is a no-op.</summary>
    private static void MigrateLegacySchema(SqliteConnection connection)
    {
        bool streetIsNotNull;
        bool hasSourceFolderCaseId;
        bool hasWorkflowState;
        using (var pragmaCommand = connection.CreateCommand())
        {
            pragmaCommand.CommandText = "PRAGMA table_info(OutboundAddressChangeRequest)";
            using var reader = pragmaCommand.ExecuteReader();
            streetIsNotNull = false;
            hasSourceFolderCaseId = false;
            hasWorkflowState = false;
            while (reader.Read())
            {
                var columnName = reader.GetString(1);
                if (string.Equals(columnName, "Street", StringComparison.OrdinalIgnoreCase))
                {
                    streetIsNotNull = reader.GetInt32(3) == 1;
                }
                else if (string.Equals(columnName, "SourceFolderCaseId", StringComparison.OrdinalIgnoreCase))
                {
                    hasSourceFolderCaseId = true;
                }
                else if (string.Equals(columnName, "WorkflowState", StringComparison.OrdinalIgnoreCase))
                {
                    hasWorkflowState = true;
                }
            }
        }

        if (streetIsNotNull)
        {
            // The rebuilt table already includes SourceFolderCaseId/WorkflowState and their index,
            // so nothing else is needed after this regardless of what the old table happened to have.
            RebuildWithNullableStreetAndNumber(connection);
            return;
        }

        if (!hasSourceFolderCaseId)
        {
            using var alterCommand = connection.CreateCommand();
            alterCommand.CommandText = "ALTER TABLE OutboundAddressChangeRequest ADD COLUMN SourceFolderCaseId INTEGER NULL";
            alterCommand.ExecuteNonQuery();
        }

        if (!hasWorkflowState)
        {
            using var alterCommand = connection.CreateCommand();
            alterCommand.CommandText = "ALTER TABLE OutboundAddressChangeRequest ADD COLUMN WorkflowState TEXT NULL";
            alterCommand.ExecuteNonQuery();
        }

        // The column is only guaranteed to exist from here on — creating this index in the same
        // statement batch as the initial CREATE TABLE IF NOT EXISTS would fail against a
        // pre-existing table that doesn't have the column yet.
        using var indexCommand = connection.CreateCommand();
        indexCommand.CommandText = "CREATE INDEX IF NOT EXISTS IX_OutboundAddressChangeRequest_SourceFolderCaseId ON OutboundAddressChangeRequest (SourceFolderCaseId)";
        indexCommand.ExecuteNonQuery();
    }

    private static void RebuildWithNullableStreetAndNumber(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                CREATE TABLE OutboundAddressChangeRequest_new (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    FullName TEXT NOT NULL,
                    Rut TEXT NOT NULL,
                    Phone TEXT NULL,
                    Street TEXT NULL,
                    Number TEXT NULL,
                    Unit TEXT NULL,
                    DestinationComuna TEXT NOT NULL,
                    Status TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    SentAt TEXT NULL,
                    SentByUserId INTEGER NULL,
                    CreatedByUserId INTEGER NOT NULL,
                    SourceFolderCaseId INTEGER NULL,
                    WorkflowState TEXT NULL
                );
                INSERT INTO OutboundAddressChangeRequest_new
                    (Id, FullName, Rut, Phone, Street, Number, Unit, DestinationComuna, Status, CreatedAt, SentAt, SentByUserId, CreatedByUserId)
                SELECT Id, FullName, Rut, Phone, Street, Number, Unit, DestinationComuna, Status, CreatedAt, SentAt, SentByUserId, CreatedByUserId
                FROM OutboundAddressChangeRequest;
                DROP TABLE OutboundAddressChangeRequest;
                ALTER TABLE OutboundAddressChangeRequest_new RENAME TO OutboundAddressChangeRequest;
                CREATE INDEX IF NOT EXISTS IX_OutboundAddressChangeRequest_Status ON OutboundAddressChangeRequest (Status);
                CREATE INDEX IF NOT EXISTS IX_OutboundAddressChangeRequest_SourceFolderCaseId ON OutboundAddressChangeRequest (SourceFolderCaseId);
                """;
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public long Insert(OutboundAddressChangeRequest request)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO OutboundAddressChangeRequest
                (FullName, Rut, Phone, Street, Number, Unit, DestinationComuna, Status, CreatedAt, SentAt, SentByUserId, CreatedByUserId, SourceFolderCaseId, WorkflowState)
            VALUES
                ($fullName, $rut, $phone, $street, $number, $unit, $destinationComuna, $status, $createdAt, $sentAt, $sentByUserId, $createdByUserId, $sourceFolderCaseId, $workflowState);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$fullName", request.FullName);
        command.Parameters.AddWithValue("$rut", request.Rut);
        command.Parameters.AddWithValue("$phone", (object?)request.Phone ?? DBNull.Value);
        command.Parameters.AddWithValue("$street", (object?)request.Street ?? DBNull.Value);
        command.Parameters.AddWithValue("$number", (object?)request.Number ?? DBNull.Value);
        command.Parameters.AddWithValue("$unit", (object?)request.Unit ?? DBNull.Value);
        command.Parameters.AddWithValue("$destinationComuna", request.DestinationComuna);
        // Always inserted as Borrador regardless of what the caller set — creation is always a draft.
        command.Parameters.AddWithValue("$status", OutboundRequestStatus.Borrador.ToString());
        command.Parameters.AddWithValue("$createdAt", request.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$sentAt", (object?)request.SentAt?.ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$sentByUserId", (object?)request.SentByUserId ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdByUserId", request.CreatedByUserId);
        command.Parameters.AddWithValue("$sourceFolderCaseId", (object?)request.SourceFolderCaseId ?? DBNull.Value);
        command.Parameters.AddWithValue("$workflowState", (object?)request.WorkflowState?.ToString() ?? DBNull.Value);

        return (long)command.ExecuteScalar()!;
    }

    public void Update(OutboundAddressChangeRequest request)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE OutboundAddressChangeRequest
            SET FullName = $fullName, Rut = $rut, Phone = $phone, Street = $street, Number = $number,
                Unit = $unit, DestinationComuna = $destinationComuna, WorkflowState = $workflowState
            WHERE Id = $id
            """;
        command.Parameters.AddWithValue("$fullName", request.FullName);
        command.Parameters.AddWithValue("$rut", request.Rut);
        command.Parameters.AddWithValue("$phone", (object?)request.Phone ?? DBNull.Value);
        command.Parameters.AddWithValue("$street", (object?)request.Street ?? DBNull.Value);
        command.Parameters.AddWithValue("$number", (object?)request.Number ?? DBNull.Value);
        command.Parameters.AddWithValue("$unit", (object?)request.Unit ?? DBNull.Value);
        command.Parameters.AddWithValue("$destinationComuna", request.DestinationComuna);
        command.Parameters.AddWithValue("$workflowState", (object?)request.WorkflowState?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$id", request.Id);
        command.ExecuteNonQuery();
    }

    public OutboundAddressChangeRequest? FindById(long id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM OutboundAddressChangeRequest WHERE Id = $id LIMIT 1";
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public IReadOnlyList<OutboundAddressChangeRequest> GetAll()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM OutboundAddressChangeRequest ORDER BY Id DESC";
        using var reader = command.ExecuteReader();
        var results = new List<OutboundAddressChangeRequest>();
        while (reader.Read())
        {
            results.Add(Map(reader));
        }
        return results;
    }

    public IReadOnlyList<OutboundAddressChangeRequest> FindBySourceFolderCaseId(long folderCaseId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM OutboundAddressChangeRequest WHERE SourceFolderCaseId = $folderCaseId ORDER BY Id DESC";
        command.Parameters.AddWithValue("$folderCaseId", folderCaseId);
        using var reader = command.ExecuteReader();
        var results = new List<OutboundAddressChangeRequest>();
        while (reader.Read())
        {
            results.Add(Map(reader));
        }
        return results;
    }

    public bool MarkSent(long id, DateTimeOffset sentAt, long sentByUserId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE OutboundAddressChangeRequest
            SET Status = 'Enviada', SentAt = $sentAt, SentByUserId = $sentByUserId
            WHERE Id = $id AND Status = 'Borrador'
            """;
        command.Parameters.AddWithValue("$sentAt", sentAt.ToString("O"));
        command.Parameters.AddWithValue("$sentByUserId", sentByUserId);
        command.Parameters.AddWithValue("$id", id);
        return command.ExecuteNonQuery() > 0;
    }

    public void Delete(long id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM OutboundAddressChangeRequest WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public long AddAttachment(OutboundAddressChangeAttachment attachment)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO OutboundAddressChangeAttachment
                (RequestId, FileName, StoredPath, ContentType, UploadedAt)
            VALUES
                ($requestId, $fileName, $storedPath, $contentType, $uploadedAt);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$requestId", attachment.RequestId);
        command.Parameters.AddWithValue("$fileName", attachment.FileName);
        command.Parameters.AddWithValue("$storedPath", attachment.StoredPath);
        command.Parameters.AddWithValue("$contentType", attachment.ContentType);
        command.Parameters.AddWithValue("$uploadedAt", attachment.UploadedAt.ToString("O"));

        return (long)command.ExecuteScalar()!;
    }

    public IReadOnlyList<OutboundAddressChangeAttachment> GetAttachments(long requestId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM OutboundAddressChangeAttachment WHERE RequestId = $requestId ORDER BY Id";
        command.Parameters.AddWithValue("$requestId", requestId);
        using var reader = command.ExecuteReader();
        var results = new List<OutboundAddressChangeAttachment>();
        while (reader.Read())
        {
            results.Add(MapAttachment(reader));
        }
        return results;
    }

    public void DeleteAttachment(long attachmentId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM OutboundAddressChangeAttachment WHERE Id = $attachmentId";
        command.Parameters.AddWithValue("$attachmentId", attachmentId);
        command.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }

    private static OutboundAddressChangeRequest Map(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(reader.GetOrdinal("Id")),
        FullName = reader.GetString(reader.GetOrdinal("FullName")),
        Rut = reader.GetString(reader.GetOrdinal("Rut")),
        Phone = reader.IsDBNull(reader.GetOrdinal("Phone")) ? null : reader.GetString(reader.GetOrdinal("Phone")),
        Street = reader.IsDBNull(reader.GetOrdinal("Street")) ? null : reader.GetString(reader.GetOrdinal("Street")),
        Number = reader.IsDBNull(reader.GetOrdinal("Number")) ? null : reader.GetString(reader.GetOrdinal("Number")),
        Unit = reader.IsDBNull(reader.GetOrdinal("Unit")) ? null : reader.GetString(reader.GetOrdinal("Unit")),
        DestinationComuna = reader.GetString(reader.GetOrdinal("DestinationComuna")),
        Status = Enum.Parse<OutboundRequestStatus>(reader.GetString(reader.GetOrdinal("Status"))),
        CreatedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("CreatedAt"))),
        SentAt = reader.IsDBNull(reader.GetOrdinal("SentAt")) ? null : DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("SentAt"))),
        SentByUserId = reader.IsDBNull(reader.GetOrdinal("SentByUserId")) ? null : reader.GetInt64(reader.GetOrdinal("SentByUserId")),
        CreatedByUserId = reader.GetInt64(reader.GetOrdinal("CreatedByUserId")),
        SourceFolderCaseId = reader.IsDBNull(reader.GetOrdinal("SourceFolderCaseId")) ? null : reader.GetInt64(reader.GetOrdinal("SourceFolderCaseId")),
        // TryParse, no Parse: un valor que ya no coincide con ningún miembro de FolderState (el
        // enum cambió, o la fila se editó a mano) no debe tirar abajo el listado entero — se lee
        // como sin estado en vez de reventar GetAll()/FindById() para todas las filas.
        WorkflowState = !reader.IsDBNull(reader.GetOrdinal("WorkflowState"))
            && Enum.TryParse<FolderState>(reader.GetString(reader.GetOrdinal("WorkflowState")), out var workflowState)
                ? workflowState
                : null
    };

    private static OutboundAddressChangeAttachment MapAttachment(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(reader.GetOrdinal("Id")),
        RequestId = reader.GetInt64(reader.GetOrdinal("RequestId")),
        FileName = reader.GetString(reader.GetOrdinal("FileName")),
        StoredPath = reader.GetString(reader.GetOrdinal("StoredPath")),
        ContentType = reader.GetString(reader.GetOrdinal("ContentType")),
        UploadedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("UploadedAt")))
    };
}
