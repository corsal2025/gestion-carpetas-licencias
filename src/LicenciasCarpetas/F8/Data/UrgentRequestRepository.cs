using Microsoft.Data.Sqlite;
using LicenciasCarpetas.F8.Domain;

namespace LicenciasCarpetas.F8.Data;

/// <summary>
/// Raw-SQL repository over Microsoft.Data.Sqlite, mirroring the reference project's
/// pattern: sealed class + primary-constructor connection string, each method opens
/// its own connection, EnsureSchema runs idempotent CREATE TABLE IF NOT EXISTS.
/// </summary>
public sealed class UrgentRequestRepository(string connectionString) : IUrgentRequestRepository
{
    public void EnsureSchema()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS UrgentRequest (
                Id                    INTEGER PRIMARY KEY AUTOINCREMENT,
                FechaPeticion         TEXT NULL,
                Nombres               TEXT NULL,
                Apellidos             TEXT NULL,
                NombreCompleto        TEXT NULL,
                Rut                   TEXT NULL,
                RutRaw                TEXT NULL,
                FechaUltimaCarpeta    TEXT NULL,
                CodigoF8              TEXT NULL,
                FechaPenultimaCarpeta TEXT NULL,
                Estado                TEXT NULL,
                EstadoActual          TEXT NULL,
                FechaDeSubida         TEXT NULL,
                SourceSheet           TEXT NULL,
                SourceRowNumber       INTEGER NULL,
                Origin                TEXT NOT NULL,
                NeedsReview           INTEGER NOT NULL DEFAULT 0,
                CreatedAt             TEXT NOT NULL,
                UpdatedAt             TEXT NULL,
                Marked                INTEGER NOT NULL DEFAULT 0,
                MarkedAt              TEXT NULL,
                SectorPdfGeneratedAt  TEXT NULL,
                PendienteCarpeta      INTEGER NOT NULL DEFAULT 0,
                MatrizSector          TEXT NULL,
                PendienteEscrituraExcel INTEGER NOT NULL DEFAULT 0,
                ImpresoMensualAt      TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_UrgentRequest_Rut ON UrgentRequest (Rut);
            CREATE INDEX IF NOT EXISTS IX_UrgentRequest_NeedsReview ON UrgentRequest (NeedsReview);

            CREATE TABLE IF NOT EXISTS ImportFlag (
                Id            INTEGER PRIMARY KEY AUTOINCREMENT,
                RequestId     INTEGER NOT NULL,
                ColumnName    TEXT NOT NULL,
                ReasonCode    TEXT NOT NULL,
                RawValue      TEXT NULL,
                CreatedAt     TEXT NOT NULL,
                FOREIGN KEY (RequestId) REFERENCES UrgentRequest (Id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS IX_ImportFlag_RequestId ON ImportFlag (RequestId);

            CREATE TABLE IF NOT EXISTS ImportRun (
                Id           INTEGER PRIMARY KEY AUTOINCREMENT,
                SourceFile   TEXT NOT NULL,
                CompletedAt  TEXT NOT NULL,
                RowsImported INTEGER NOT NULL,
                RowsFlagged  INTEGER NOT NULL
            );
            """;
        command.ExecuteNonQuery();

        // CREATE TABLE IF NOT EXISTS above only shapes brand-new databases — existing ones
        // created before Marked/PendienteCarpeta/etc. were added need these columns backfilled.
        EnsureColumnExists(connection, "UrgentRequest", "Marked", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumnExists(connection, "UrgentRequest", "MarkedAt", "TEXT NULL");
        EnsureColumnExists(connection, "UrgentRequest", "SectorPdfGeneratedAt", "TEXT NULL");
        EnsureColumnExists(connection, "UrgentRequest", "PendienteCarpeta", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumnExists(connection, "UrgentRequest", "MatrizSector", "TEXT NULL");
        EnsureColumnExists(connection, "UrgentRequest", "PendienteEscrituraExcel", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumnExists(connection, "UrgentRequest", "ImpresoMensualAt", "TEXT NULL");
    }

    private static void EnsureColumnExists(SqliteConnection connection, string table, string column, string definition)
    {
        using (var check = connection.CreateCommand())
        {
            check.CommandText = $"PRAGMA table_info({table})";
            using var reader = check.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(reader.GetOrdinal("name")), column, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
        }

        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition}";
        alter.ExecuteNonQuery();
    }

    public long Insert(UrgentRequest request)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO UrgentRequest
                (FechaPeticion, Nombres, Apellidos, NombreCompleto, Rut, RutRaw, FechaUltimaCarpeta, CodigoF8,
                 FechaPenultimaCarpeta, Estado, EstadoActual, FechaDeSubida, SourceSheet, SourceRowNumber, Origin,
                 NeedsReview, CreatedAt, UpdatedAt, MatrizSector, PendienteEscrituraExcel)
            VALUES
                ($fechaPeticion, $nombres, $apellidos, $nombreCompleto, $rut, $rutRaw, $fechaUltimaCarpeta, $codigoF8,
                 $fechaPenultimaCarpeta, $estado, $estadoActual, $fechaDeSubida, $sourceSheet, $sourceRowNumber, $origin,
                 $needsReview, $createdAt, $updatedAt, $matrizSector, $pendienteEscrituraExcel);
            SELECT last_insert_rowid();
            """;
        BindParameters(command, request);
        return (long)command.ExecuteScalar()!;
    }

    public UrgentRequest? FindById(long id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM UrgentRequest WHERE Id = $id LIMIT 1";
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public void Update(UrgentRequest request)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE UrgentRequest
            SET FechaPeticion = $fechaPeticion, Nombres = $nombres, Apellidos = $apellidos,
                NombreCompleto = $nombreCompleto, Rut = $rut, RutRaw = $rutRaw,
                FechaUltimaCarpeta = $fechaUltimaCarpeta, CodigoF8 = $codigoF8,
                FechaPenultimaCarpeta = $fechaPenultimaCarpeta, Estado = $estado, EstadoActual = $estadoActual,
                FechaDeSubida = $fechaDeSubida, SourceSheet = $sourceSheet, SourceRowNumber = $sourceRowNumber,
                Origin = $origin, NeedsReview = $needsReview, UpdatedAt = $now,
                MatrizSector = $matrizSector, PendienteEscrituraExcel = $pendienteEscrituraExcel
            WHERE Id = $id
            """;
        BindParameters(command, request);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$id", request.Id);
        command.ExecuteNonQuery();
    }

    public void Delete(long id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM UrgentRequest WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<UrgentRequest> GetAll()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM UrgentRequest ORDER BY Id";
        using var reader = command.ExecuteReader();
        var results = new List<UrgentRequest>();
        while (reader.Read())
        {
            results.Add(Map(reader));
        }
        return results;
    }

    public IReadOnlyList<UrgentRequest> Query(UrgentRequestFilter filter, string? search)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        var where = new List<string>();

        if (!string.IsNullOrWhiteSpace(filter.Month))
        {
            where.Add("substr(FechaPeticion, 1, 7) = $month");
            command.Parameters.AddWithValue("$month", filter.Month);
        }

        if (!string.IsNullOrWhiteSpace(filter.Estado))
        {
            where.Add("Estado = $estado");
            command.Parameters.AddWithValue("$estado", filter.Estado);
        }

        if (!string.IsNullOrWhiteSpace(filter.EstadoActual))
        {
            where.Add("EstadoActual = $estadoActual");
            command.Parameters.AddWithValue("$estadoActual", filter.EstadoActual);
        }

        if (filter.Flagged == true)
        {
            where.Add("NeedsReview = 1");
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            where.Add("(Rut = $searchRut OR NombreCompleto LIKE $searchName OR Nombres LIKE $searchName OR Apellidos LIKE $searchName)");
            var normalizedRut = Rut.TryParse(search, out var rut) ? rut.ToString() : "\0no-match\0";
            command.Parameters.AddWithValue("$searchRut", normalizedRut);
            command.Parameters.AddWithValue("$searchName", $"%{search}%");
        }

        command.CommandText = "SELECT * FROM UrgentRequest" +
            (where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : string.Empty) +
            " ORDER BY Id";

        using var reader = command.ExecuteReader();
        var results = new List<UrgentRequest>();
        while (reader.Read())
        {
            results.Add(Map(reader));
        }
        return results;
    }

    public void AddFlag(long requestId, string columnName, string reasonCode, string? rawValue)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();

        using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO ImportFlag (RequestId, ColumnName, ReasonCode, RawValue, CreatedAt)
                VALUES ($requestId, $columnName, $reasonCode, $rawValue, $createdAt)
                """;
            insert.Parameters.AddWithValue("$requestId", requestId);
            insert.Parameters.AddWithValue("$columnName", columnName);
            insert.Parameters.AddWithValue("$reasonCode", reasonCode);
            insert.Parameters.AddWithValue("$rawValue", (object?)rawValue ?? DBNull.Value);
            insert.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O"));
            insert.ExecuteNonQuery();
        }

        using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = "UPDATE UrgentRequest SET NeedsReview = 1 WHERE Id = $id";
            update.Parameters.AddWithValue("$id", requestId);
            update.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public IReadOnlyList<ImportFlag> GetFlagsFor(long requestId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM ImportFlag WHERE RequestId = $requestId ORDER BY Id";
        command.Parameters.AddWithValue("$requestId", requestId);
        using var reader = command.ExecuteReader();
        var results = new List<ImportFlag>();
        while (reader.Read())
        {
            results.Add(MapFlag(reader));
        }
        return results;
    }

    public IReadOnlyList<UrgentRequest> GetFlagged()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM UrgentRequest WHERE NeedsReview = 1 ORDER BY Id";
        using var reader = command.ExecuteReader();
        var results = new List<UrgentRequest>();
        while (reader.Read())
        {
            results.Add(Map(reader));
        }
        return results;
    }

    public void ClearFlags(long requestId)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();

        using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM ImportFlag WHERE RequestId = $id";
            delete.Parameters.AddWithValue("$id", requestId);
            delete.ExecuteNonQuery();
        }

        using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = "UPDATE UrgentRequest SET NeedsReview = 0 WHERE Id = $id";
            update.Parameters.AddWithValue("$id", requestId);
            update.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public bool HasCompletedImport(string sourceFile)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM ImportRun WHERE SourceFile = $sourceFile LIMIT 1";
        command.Parameters.AddWithValue("$sourceFile", sourceFile);
        return command.ExecuteScalar() is not null;
    }

    public void RecordImportRun(string sourceFile, int rowsImported, int rowsFlagged)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ImportRun (SourceFile, CompletedAt, RowsImported, RowsFlagged)
            VALUES ($sourceFile, $completedAt, $rowsImported, $rowsFlagged)
            """;
        command.Parameters.AddWithValue("$sourceFile", sourceFile);
        command.Parameters.AddWithValue("$completedAt", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$rowsImported", rowsImported);
        command.Parameters.AddWithValue("$rowsFlagged", rowsFlagged);
        command.ExecuteNonQuery();
    }

    public void DeleteImportedRows()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM UrgentRequest WHERE Origin = 'Import'";
        command.ExecuteNonQuery();
    }

    public void SetMarked(long id, bool marked)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        // Re-marking (marked=true) resets SectorPdfGeneratedAt so the case is pulled back into
        // the next print batch instead of staying excluded from an earlier run. Marked and
        // PendienteCarpeta are mutually exclusive, so marking one always clears the other —
        // enforced here (not just client-side) so the two can never both end up true.
        command.CommandText = marked
            ? "UPDATE UrgentRequest SET Marked = 1, MarkedAt = $at, SectorPdfGeneratedAt = NULL, PendienteCarpeta = 0 WHERE Id = $id"
            : "UPDATE UrgentRequest SET Marked = 0, MarkedAt = NULL WHERE Id = $id";
        if (marked)
        {
            command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        }
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public void SetPendienteCarpeta(long id, bool pendienteCarpeta)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = pendienteCarpeta
            ? "UPDATE UrgentRequest SET PendienteCarpeta = 1, Marked = 0, MarkedAt = NULL WHERE Id = $id"
            : "UPDATE UrgentRequest SET PendienteCarpeta = 0 WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public void SetSectorPdfGenerated(long id, DateTimeOffset generatedAt)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE UrgentRequest SET SectorPdfGeneratedAt = $at WHERE Id = $id";
        command.Parameters.AddWithValue("$at", generatedAt.ToString("O"));
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public void SetImpresoMensual(long id, DateTimeOffset? generatedAt)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE UrgentRequest SET ImpresoMensualAt = $at WHERE Id = $id";
        command.Parameters.AddWithValue("$at", (object?)generatedAt?.ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public UrgentRequest? FindByRut(string rut)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM UrgentRequest WHERE Rut = $rut LIMIT 1";
        command.Parameters.AddWithValue("$rut", rut);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public void SetPendienteEscrituraExcel(long id, bool pendiente)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE UrgentRequest SET PendienteEscrituraExcel = $pendiente WHERE Id = $id";
        command.Parameters.AddWithValue("$pendiente", pendiente ? 1 : 0);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<UrgentRequest> GetPendingEscrituraExcel()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM UrgentRequest WHERE PendienteEscrituraExcel = 1 ORDER BY Id";
        using var reader = command.ExecuteReader();
        var results = new List<UrgentRequest>();
        while (reader.Read())
        {
            results.Add(Map(reader));
        }
        return results;
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys = ON";
            pragma.ExecuteNonQuery();
        }
        return connection;
    }

    private static void BindParameters(SqliteCommand command, UrgentRequest request)
    {
        command.Parameters.AddWithValue("$fechaPeticion", (object?)request.FechaPeticion?.ToString("yyyy-MM-dd") ?? DBNull.Value);
        command.Parameters.AddWithValue("$nombres", (object?)request.Nombres ?? DBNull.Value);
        command.Parameters.AddWithValue("$apellidos", (object?)request.Apellidos ?? DBNull.Value);
        command.Parameters.AddWithValue("$nombreCompleto", (object?)request.NombreCompleto ?? DBNull.Value);
        command.Parameters.AddWithValue("$rut", (object?)request.Rut ?? DBNull.Value);
        command.Parameters.AddWithValue("$rutRaw", (object?)request.RutRaw ?? DBNull.Value);
        command.Parameters.AddWithValue("$fechaUltimaCarpeta", (object?)request.FechaUltimaCarpeta?.ToString("yyyy-MM-dd") ?? DBNull.Value);
        command.Parameters.AddWithValue("$codigoF8", (object?)request.CodigoF8 ?? DBNull.Value);
        command.Parameters.AddWithValue("$fechaPenultimaCarpeta", (object?)request.FechaPenultimaCarpeta?.ToString("yyyy-MM-dd") ?? DBNull.Value);
        command.Parameters.AddWithValue("$estado", (object?)request.Estado ?? DBNull.Value);
        command.Parameters.AddWithValue("$estadoActual", (object?)request.EstadoActual ?? DBNull.Value);
        command.Parameters.AddWithValue("$fechaDeSubida", (object?)request.FechaDeSubida?.ToString("yyyy-MM-dd") ?? DBNull.Value);
        command.Parameters.AddWithValue("$sourceSheet", (object?)request.SourceSheet ?? DBNull.Value);
        command.Parameters.AddWithValue("$sourceRowNumber", (object?)request.SourceRowNumber ?? DBNull.Value);
        command.Parameters.AddWithValue("$origin", request.Origin);
        command.Parameters.AddWithValue("$needsReview", request.NeedsReview ? 1 : 0);
        command.Parameters.AddWithValue("$createdAt", request.CreatedAt == default ? DateTimeOffset.UtcNow.ToString("O") : request.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", (object?)request.UpdatedAt?.ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$matrizSector", (object?)request.MatrizSector ?? DBNull.Value);
        command.Parameters.AddWithValue("$pendienteEscrituraExcel", request.PendienteEscrituraExcel ? 1 : 0);
    }

    private static UrgentRequest Map(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(reader.GetOrdinal("Id")),
        FechaPeticion = ReadDateOnly(reader, "FechaPeticion"),
        Nombres = ReadString(reader, "Nombres"),
        Apellidos = ReadString(reader, "Apellidos"),
        NombreCompleto = ReadString(reader, "NombreCompleto"),
        Rut = ReadString(reader, "Rut"),
        RutRaw = ReadString(reader, "RutRaw"),
        FechaUltimaCarpeta = ReadDateOnly(reader, "FechaUltimaCarpeta"),
        CodigoF8 = ReadString(reader, "CodigoF8"),
        FechaPenultimaCarpeta = ReadDateOnly(reader, "FechaPenultimaCarpeta"),
        Estado = ReadString(reader, "Estado"),
        EstadoActual = ReadString(reader, "EstadoActual"),
        FechaDeSubida = ReadDateOnly(reader, "FechaDeSubida"),
        SourceSheet = ReadString(reader, "SourceSheet"),
        SourceRowNumber = reader.IsDBNull(reader.GetOrdinal("SourceRowNumber")) ? null : reader.GetInt32(reader.GetOrdinal("SourceRowNumber")),
        Origin = reader.GetString(reader.GetOrdinal("Origin")),
        NeedsReview = reader.GetInt32(reader.GetOrdinal("NeedsReview")) == 1,
        CreatedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("CreatedAt"))),
        UpdatedAt = reader.IsDBNull(reader.GetOrdinal("UpdatedAt")) ? null : DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("UpdatedAt"))),
        Marked = reader.GetInt32(reader.GetOrdinal("Marked")) == 1,
        MarkedAt = reader.IsDBNull(reader.GetOrdinal("MarkedAt")) ? null : DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("MarkedAt"))),
        SectorPdfGeneratedAt = reader.IsDBNull(reader.GetOrdinal("SectorPdfGeneratedAt")) ? null : DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("SectorPdfGeneratedAt"))),
        PendienteCarpeta = reader.GetInt32(reader.GetOrdinal("PendienteCarpeta")) == 1,
        MatrizSector = ReadString(reader, "MatrizSector"),
        PendienteEscrituraExcel = reader.GetInt32(reader.GetOrdinal("PendienteEscrituraExcel")) == 1,
        ImpresoMensualAt = reader.IsDBNull(reader.GetOrdinal("ImpresoMensualAt")) ? null : DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("ImpresoMensualAt"))),
    };

    private static ImportFlag MapFlag(SqliteDataReader reader) => new(
        reader.GetInt64(reader.GetOrdinal("Id")),
        reader.GetInt64(reader.GetOrdinal("RequestId")),
        reader.GetString(reader.GetOrdinal("ColumnName")),
        reader.GetString(reader.GetOrdinal("ReasonCode")),
        ReadString(reader, "RawValue"),
        DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("CreatedAt"))));

    private static string? ReadString(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static DateOnly? ReadDateOnly(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : DateOnly.Parse(reader.GetString(ordinal));
    }
}
