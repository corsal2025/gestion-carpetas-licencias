using System.Text;
using LicenciasCarpetas.Domain;
using Microsoft.Data.Sqlite;

namespace LicenciasCarpetas.Persistence;

public enum UpsertOutcome
{
    Inserted,
    Updated
}

public interface IFolderCaseRepository
{
    void EnsureSchema();
    UpsertOutcome Upsert(FolderCase folderCase);
    FolderCase? FindById(long id);
    IReadOnlyList<FolderCase> Query(CaseFilter filter, int skip, int take);
    IReadOnlyList<FolderCase> QueryAll(CaseFilter filter);
    int Count(CaseFilter filter);
    int CountNeedingReview();
    IReadOnlyList<int> DistinctYears();
    IReadOnlyList<FolderCase> ForSector(FolderSector sector, bool onlyMarked, bool includePrinted = false);

    /// <summary>Records that these cases went out on a printed sector list.</summary>
    void MarkSectorPrinted(IReadOnlyCollection<long> ids);

    /// <summary>Puts a case back on the pending list ("volver a pedir").</summary>
    void ClearSectorPrinted(long id);
    IReadOnlyList<(DateOnly Date, Office Office, int Scheduled, int Attended)> DailyAttendance(int year, int month);
    IReadOnlyList<(FolderState? State, int Count)> FolderStateBreakdown(int year, int? month, Office? office);
    IReadOnlyList<(FinalDecision? Decision, int Count)> FinalDecisionBreakdown(int year, int? month, Office? office);
    void UpdateEditableFields(long id, string? fullName, string? rut, DateOnly? citationDate,
        DateOnly? folderUploadedDate, DateOnly? lastFolderDate, string? lastFolderComuna,
        FolderState? folderState, FinalDecision? finalDecision, MoralIdoneity? moralIdoneity,
        string? attentionNote, bool needsReview);
    void SetMarked(long id, bool marked);

    /// <summary>Moves the case to the bin. Recoverable with <see cref="Restore"/>.</summary>
    void Delete(long id);

    void Restore(long id);
    void DeletePermanently(long id);
    IReadOnlyList<FolderCase> Deleted();
    int CountDeleted();
    long Insert(FolderCase folderCase);
}

/// <summary>Plain SQLite, no ORM — same approach as the sibling OutlookComunaRouter service.</summary>
public sealed class FolderCaseRepository(string connectionString) : IFolderCaseRepository
{
    public void EnsureSchema()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS FolderCase (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                CitationDate TEXT NULL,
                FolderUploadedDate TEXT NULL,
                LastFolderDate TEXT NULL,
                LastFolderComuna TEXT NULL,
                FirstName TEXT NULL,
                LastName TEXT NULL,
                FullName TEXT NULL,
                Rut TEXT NULL,
                Office INTEGER NOT NULL,
                AttentionNote TEXT NULL,
                Attended INTEGER NOT NULL DEFAULT 0,
                MoralIdoneity INTEGER NULL,
                FolderState INTEGER NULL,
                FolderStateRaw TEXT NULL,
                FinalDecision INTEGER NULL,
                FinalDecisionRaw TEXT NULL,
                SourceSheet TEXT NULL,
                SourceRow INTEGER NOT NULL DEFAULT 0,
                NeedsReview INTEGER NOT NULL DEFAULT 0,
                Marked INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_FolderCase_Rut ON FolderCase (Rut);
            CREATE INDEX IF NOT EXISTS IX_FolderCase_CitationDate ON FolderCase (CitationDate);
            CREATE INDEX IF NOT EXISTS IX_FolderCase_Source ON FolderCase (SourceSheet, SourceRow);
            CREATE INDEX IF NOT EXISTS IX_FolderCase_Natural ON FolderCase (Office, CitationDate, Rut);
            """;
        command.ExecuteNonQuery();

        AddColumnIfMissing(connection, "DeletedAt");
        AddColumnIfMissing(connection, "FullNameSort");
        AddColumnIfMissing(connection, "SectorPrintedAt");
        BackfillFullNameSort(connection);
    }

    /// <summary>Additive migration for databases created before a column existed — SQLite has no
    /// "ADD COLUMN IF NOT EXISTS", so PRAGMA table_info is checked first. Every column added this
    /// way is a nullable TEXT.</summary>
    private static void AddColumnIfMissing(SqliteConnection connection, string column)
    {
        using (var pragmaCommand = connection.CreateCommand())
        {
            pragmaCommand.CommandText = "PRAGMA table_info(FolderCase)";
            using var reader = pragmaCommand.ExecuteReader();
            var nameOrdinal = reader.GetOrdinal("name");
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(nameOrdinal), column, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
        }

        using var alterCommand = connection.CreateCommand();
        // The column name is one of the three literals above, never caller text.
        alterCommand.CommandText = $"ALTER TABLE FolderCase ADD COLUMN {column} TEXT NULL";
        alterCommand.ExecuteNonQuery();
    }


    /// <summary>
    /// Fills the sort key for rows stored before it existed. Stripping accents is not something
    /// SQLite can do, so the rows are read and rewritten here — in one transaction, because the
    /// 2026 workbook alone is over 20.000 of them.
    /// </summary>
    private static void BackfillFullNameSort(SqliteConnection connection)
    {
        var pending = new List<(long Id, string FullName)>();

        using (var selectCommand = connection.CreateCommand())
        {
            selectCommand.CommandText = "SELECT Id, FullName FROM FolderCase WHERE FullNameSort IS NULL AND FullName IS NOT NULL";
            using var reader = selectCommand.ExecuteReader();
            while (reader.Read())
            {
                pending.Add((reader.GetInt64(0), reader.GetString(1)));
            }
        }

        if (pending.Count == 0)
        {
            return;
        }

        using var transaction = connection.BeginTransaction();
        using var updateCommand = connection.CreateCommand();
        updateCommand.CommandText = "UPDATE FolderCase SET FullNameSort = $sort WHERE Id = $id";
        var sortParameter = updateCommand.Parameters.Add("$sort", SqliteType.Text);
        var idParameter = updateCommand.Parameters.Add("$id", SqliteType.Integer);

        foreach (var (id, fullName) in pending)
        {
            sortParameter.Value = TextNormalizer.Normalize(fullName);
            idParameter.Value = id;
            updateCommand.ExecuteNonQuery();
        }

        transaction.Commit();
    }


    /// <summary>
    /// Re-importing the same workbook must not duplicate anyone. A row is the same case when the
    /// person, citation date and office match; when the RUT or the date is unreadable, the workbook
    /// cell it came from (sheet + row) is used as identity instead.
    /// </summary>
    public UpsertOutcome Upsert(FolderCase folderCase)
    {
        var existingId = FindExistingId(folderCase);
        if (existingId is null)
        {
            Insert(folderCase);
            return UpsertOutcome.Inserted;
        }

        Update(existingId.Value, folderCase);
        return UpsertOutcome.Updated;
    }

    private long? FindExistingId(FolderCase folderCase)
    {
        using var connection = Open();

        if (folderCase.Rut is not null && folderCase.CitationDate is not null)
        {
            using var byNaturalKey = connection.CreateCommand();
            byNaturalKey.CommandText = """
                SELECT Id FROM FolderCase
                WHERE Office = $office AND CitationDate = $citationDate AND Rut = $rut
                LIMIT 1
                """;
            byNaturalKey.Parameters.AddWithValue("$office", (int)folderCase.Office);
            byNaturalKey.Parameters.AddWithValue("$citationDate", Text(folderCase.CitationDate));
            byNaturalKey.Parameters.AddWithValue("$rut", folderCase.Rut);
            if (byNaturalKey.ExecuteScalar() is long id)
            {
                return id;
            }
        }

        if (folderCase.SourceSheet is null)
        {
            return null;
        }

        using var bySource = connection.CreateCommand();
        bySource.CommandText = "SELECT Id FROM FolderCase WHERE SourceSheet = $sheet AND SourceRow = $row LIMIT 1";
        bySource.Parameters.AddWithValue("$sheet", folderCase.SourceSheet);
        bySource.Parameters.AddWithValue("$row", folderCase.SourceRow);
        return bySource.ExecuteScalar() as long?;
    }

    public long Insert(FolderCase folderCase)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO FolderCase (
                CitationDate, FolderUploadedDate, LastFolderDate, LastFolderComuna,
                FirstName, LastName, FullName, FullNameSort, Rut, Office, AttentionNote, Attended,
                MoralIdoneity, FolderState, FolderStateRaw, FinalDecision, FinalDecisionRaw,
                SourceSheet, SourceRow, NeedsReview, Marked, CreatedAt, UpdatedAt)
            VALUES (
                $citationDate, $uploadedDate, $lastFolderDate, $lastFolderComuna,
                $firstName, $lastName, $fullName, $fullNameSort, $rut, $office, $attention, $attended,
                $idoneity, $state, $stateRaw, $decision, $decisionRaw,
                $sheet, $row, $needsReview, $marked, $createdAt, $updatedAt);
            SELECT last_insert_rowid();
            """;
        BindWritableFields(command, folderCase);
        command.Parameters.AddWithValue("$firstName", Nullable(folderCase.FirstName));
        command.Parameters.AddWithValue("$lastName", Nullable(folderCase.LastName));
        command.Parameters.AddWithValue("$sheet", Nullable(folderCase.SourceSheet));
        command.Parameters.AddWithValue("$row", folderCase.SourceRow);
        command.Parameters.AddWithValue("$marked", folderCase.Marked ? 1 : 0);
        command.Parameters.AddWithValue("$createdAt", folderCase.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", folderCase.UpdatedAt.ToString("O"));

        return (long)command.ExecuteScalar()!;
    }

    private void Update(long id, FolderCase folderCase)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        // Marked is the operator's own bookkeeping and is deliberately left untouched by an import.
        command.CommandText = """
            UPDATE FolderCase SET
                CitationDate = $citationDate,
                FolderUploadedDate = $uploadedDate,
                LastFolderDate = $lastFolderDate,
                LastFolderComuna = $lastFolderComuna,
                FirstName = $firstName,
                LastName = $lastName,
                FullName = $fullName,
                FullNameSort = $fullNameSort,
                Rut = $rut,
                Office = $office,
                AttentionNote = $attention,
                Attended = $attended,
                MoralIdoneity = $idoneity,
                FolderState = $state,
                FolderStateRaw = $stateRaw,
                FinalDecision = $decision,
                FinalDecisionRaw = $decisionRaw,
                SourceSheet = $sheet,
                SourceRow = $row,
                NeedsReview = $needsReview,
                UpdatedAt = $updatedAt
            WHERE Id = $id
            """;
        BindWritableFields(command, folderCase);
        command.Parameters.AddWithValue("$firstName", Nullable(folderCase.FirstName));
        command.Parameters.AddWithValue("$lastName", Nullable(folderCase.LastName));
        command.Parameters.AddWithValue("$sheet", Nullable(folderCase.SourceSheet));
        command.Parameters.AddWithValue("$row", folderCase.SourceRow);
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    private static void BindWritableFields(SqliteCommand command, FolderCase folderCase)
    {
        command.Parameters.AddWithValue("$citationDate", Nullable(Text(folderCase.CitationDate)));
        command.Parameters.AddWithValue("$uploadedDate", Nullable(Text(folderCase.FolderUploadedDate)));
        command.Parameters.AddWithValue("$lastFolderDate", Nullable(Text(folderCase.LastFolderDate)));
        command.Parameters.AddWithValue("$lastFolderComuna", Nullable(folderCase.LastFolderComuna));
        command.Parameters.AddWithValue("$fullName", Nullable(folderCase.FullName));
        command.Parameters.AddWithValue("$fullNameSort", Nullable(SortKey(folderCase.FullName)));
        command.Parameters.AddWithValue("$rut", Nullable(folderCase.Rut));
        command.Parameters.AddWithValue("$office", (int)folderCase.Office);
        command.Parameters.AddWithValue("$attention", Nullable(folderCase.AttentionNote));
        command.Parameters.AddWithValue("$attended", folderCase.Attended ? 1 : 0);
        command.Parameters.AddWithValue("$idoneity", folderCase.MoralIdoneity is { } idoneity ? (int)idoneity : DBNull.Value);
        command.Parameters.AddWithValue("$state", folderCase.FolderState is { } state ? (int)state : DBNull.Value);
        command.Parameters.AddWithValue("$stateRaw", Nullable(folderCase.FolderStateRaw));
        command.Parameters.AddWithValue("$decision", folderCase.FinalDecision is { } decision ? (int)decision : DBNull.Value);
        command.Parameters.AddWithValue("$decisionRaw", Nullable(folderCase.FinalDecisionRaw));
        command.Parameters.AddWithValue("$needsReview", folderCase.NeedsReview ? 1 : 0);
    }

    public FolderCase? FindById(long id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM FolderCase WHERE Id = $id LIMIT 1";
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public IReadOnlyList<FolderCase> Query(CaseFilter filter, int skip, int take)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        var where = BuildWhere(filter, command);
        command.CommandText = $"""
            SELECT * FROM FolderCase
            {where}
            ORDER BY {OrderBy(filter)}
            LIMIT $take OFFSET $skip
            """;
        command.Parameters.AddWithValue("$take", take);
        command.Parameters.AddWithValue("$skip", skip);

        var results = new List<FolderCase>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(Map(reader));
        }
        return results;
    }

    /// <summary>Every case matching the filter, unpaged — used by the Excel export, where handing
    /// back a silently truncated file would be worse than a slow one.</summary>
    public IReadOnlyList<FolderCase> QueryAll(CaseFilter filter)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        var where = BuildWhere(filter, command);
        command.CommandText = $"""
            SELECT * FROM FolderCase
            {where}
            ORDER BY {OrderBy(filter)}
            """;

        var results = new List<FolderCase>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(Map(reader));
        }
        return results;
    }

    public int Count(CaseFilter filter)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        var where = BuildWhere(filter, command);
        command.CommandText = $"SELECT COUNT(*) FROM FolderCase {where}";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public int CountNeedingReview()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM FolderCase WHERE NeedsReview = 1 AND DeletedAt IS NULL";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public int CountDeleted()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM FolderCase WHERE DeletedAt IS NOT NULL";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    /// <summary>Everything currently in the bin, most recently deleted first.</summary>
    public IReadOnlyList<FolderCase> Deleted()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT * FROM FolderCase
            WHERE DeletedAt IS NOT NULL
            ORDER BY DeletedAt DESC
            LIMIT 500
            """;

        var results = new List<FolderCase>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(Map(reader));
        }
        return results;
    }

    public IReadOnlyList<int> DistinctYears()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT DISTINCT substr(CitationDate, 1, 4) AS Year
            FROM FolderCase WHERE CitationDate IS NOT NULL AND DeletedAt IS NULL
            ORDER BY Year DESC
            """;
        var years = new List<int>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (int.TryParse(reader.GetString(0), out var year))
            {
                years.Add(year);
            }
        }
        return years;
    }

    /// <summary>Cases whose physical folder has to be pulled from a given sector, for the printable
    /// list. Folders already requested are left out unless explicitly asked for.</summary>
    public IReadOnlyList<FolderCase> ForSector(FolderSector sector, bool onlyMarked, bool includePrinted = false)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        var cutoff = new DateOnly(2023, 7, 1).ToString("yyyy-MM-dd");
        var sectorClause = sector == FolderSector.Archivo
            ? "LastFolderDate < $cutoff"
            : "LastFolderDate >= $cutoff";
        var markedClause = onlyMarked ? "AND Marked = 1" : string.Empty;
        var printedClause = includePrinted ? string.Empty : "AND SectorPrintedAt IS NULL";

        command.CommandText = $"""
            SELECT * FROM FolderCase
            WHERE DeletedAt IS NULL AND LastFolderDate IS NOT NULL
              AND {sectorClause} {markedClause} {printedClause}
            ORDER BY CitationDate DESC, FullNameSort COLLATE NOCASE ASC
            LIMIT 2000
            """;
        command.Parameters.AddWithValue("$cutoff", cutoff);

        var results = new List<FolderCase>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(Map(reader));
        }
        return results;
    }

    /// <summary>Scheduled vs attended people per day and office — the agenda half of the statistics screen.</summary>
    public IReadOnlyList<(DateOnly Date, Office Office, int Scheduled, int Attended)> DailyAttendance(int year, int month)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT CitationDate, Office, COUNT(*) AS Scheduled, SUM(Attended) AS Attended
            FROM FolderCase
            WHERE DeletedAt IS NULL AND CitationDate IS NOT NULL
              AND substr(CitationDate, 1, 4) = $year
              AND substr(CitationDate, 6, 2) = $month
            GROUP BY CitationDate, Office
            ORDER BY CitationDate ASC, Office ASC
            """;
        command.Parameters.AddWithValue("$year", year.ToString("D4"));
        command.Parameters.AddWithValue("$month", month.ToString("D2"));

        var results = new List<(DateOnly, Office, int, int)>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add((
                DateOnly.Parse(reader.GetString(0)),
                (Office)reader.GetInt32(1),
                Convert.ToInt32(reader.GetValue(2)),
                reader.IsDBNull(3) ? 0 : Convert.ToInt32(reader.GetValue(3))));
        }
        return results;
    }

    public IReadOnlyList<(FolderState? State, int Count)> FolderStateBreakdown(int year, int? month, Office? office)
        => Breakdown("FolderState", year, month, office)
            .Select(entry => (entry.Value is { } value ? (FolderState)value : (FolderState?)null, entry.Count))
            .ToList();

    public IReadOnlyList<(FinalDecision? Decision, int Count)> FinalDecisionBreakdown(int year, int? month, Office? office)
        => Breakdown("FinalDecision", year, month, office)
            .Select(entry => (entry.Value is { } value ? (FinalDecision)value : (FinalDecision?)null, entry.Count))
            .ToList();

    /// <summary>Counts per catalog value. The column name is never caller-supplied text — only the two
    /// literals below reach it — so it cannot carry SQL injection.</summary>
    private List<(int? Value, int Count)> Breakdown(string column, int year, int? month, Office? office)
    {
        if (column is not ("FolderState" or "FinalDecision"))
        {
            throw new ArgumentOutOfRangeException(nameof(column), column, "Unsupported breakdown column.");
        }

        using var connection = Open();
        using var command = connection.CreateCommand();
        var monthClause = month is null ? string.Empty : "AND substr(CitationDate, 6, 2) = $month";
        var officeClause = office is null ? string.Empty : "AND Office = $office";
        command.CommandText = $"""
            SELECT {column} AS Value, COUNT(*) AS Total
            FROM FolderCase
            WHERE DeletedAt IS NULL AND CitationDate IS NOT NULL
              AND substr(CitationDate, 1, 4) = $year {monthClause} {officeClause}
            GROUP BY {column}
            ORDER BY Total DESC
            """;
        command.Parameters.AddWithValue("$year", year.ToString("D4"));
        if (month is { } monthValue)
        {
            command.Parameters.AddWithValue("$month", monthValue.ToString("D2"));
        }
        if (office is { } officeValue)
        {
            command.Parameters.AddWithValue("$office", (int)officeValue);
        }

        var results = new List<(int?, int)>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add((
                reader.IsDBNull(0) ? null : reader.GetInt32(0),
                Convert.ToInt32(reader.GetValue(1))));
        }
        return results;
    }

    public void UpdateEditableFields(long id, string? fullName, string? rut, DateOnly? citationDate,
        DateOnly? folderUploadedDate, DateOnly? lastFolderDate, string? lastFolderComuna,
        FolderState? folderState, FinalDecision? finalDecision, MoralIdoneity? moralIdoneity,
        string? attentionNote, bool needsReview)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE FolderCase SET
                FullName = $fullName,
                FullNameSort = $fullNameSort,
                Rut = $rut,
                CitationDate = $citationDate,
                FolderUploadedDate = $uploadedDate,
                LastFolderDate = $lastFolderDate,
                LastFolderComuna = $lastFolderComuna,
                FolderState = $state,
                FolderStateRaw = NULL,
                FinalDecision = $decision,
                FinalDecisionRaw = NULL,
                MoralIdoneity = $idoneity,
                AttentionNote = $attention,
                Attended = $attended,
                NeedsReview = $needsReview,
                UpdatedAt = $updatedAt
            WHERE Id = $id
            """;
        command.Parameters.AddWithValue("$fullName", Nullable(fullName));
        command.Parameters.AddWithValue("$fullNameSort", Nullable(SortKey(fullName)));
        command.Parameters.AddWithValue("$rut", Nullable(rut));
        command.Parameters.AddWithValue("$citationDate", Nullable(Text(citationDate)));
        command.Parameters.AddWithValue("$uploadedDate", Nullable(Text(folderUploadedDate)));
        command.Parameters.AddWithValue("$lastFolderDate", Nullable(Text(lastFolderDate)));
        command.Parameters.AddWithValue("$lastFolderComuna", Nullable(lastFolderComuna));
        command.Parameters.AddWithValue("$state", folderState is { } state ? (int)state : DBNull.Value);
        command.Parameters.AddWithValue("$decision", finalDecision is { } decision ? (int)decision : DBNull.Value);
        command.Parameters.AddWithValue("$idoneity", moralIdoneity is { } idoneity ? (int)idoneity : DBNull.Value);
        command.Parameters.AddWithValue("$attention", Nullable(attentionNote));
        command.Parameters.AddWithValue("$attended", attentionNote is not null ? 1 : 0);
        command.Parameters.AddWithValue("$needsReview", needsReview ? 1 : 0);
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public void MarkSectorPrinted(IReadOnlyCollection<long> ids)
    {
        if (ids.Count == 0)
        {
            return;
        }

        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE FolderCase SET SectorPrintedAt = $printedAt WHERE Id = $id";
        var printedAt = command.Parameters.Add("$printedAt", SqliteType.Text);
        var idParameter = command.Parameters.Add("$id", SqliteType.Integer);
        printedAt.Value = DateTimeOffset.UtcNow.ToString("O");

        foreach (var id in ids)
        {
            idParameter.Value = id;
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public void ClearSectorPrinted(long id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE FolderCase SET SectorPrintedAt = NULL WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public void SetMarked(long id, bool marked)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE FolderCase SET Marked = $marked, UpdatedAt = $updatedAt WHERE Id = $id";
        command.Parameters.AddWithValue("$marked", marked ? 1 : 0);
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public void Delete(long id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE FolderCase SET DeletedAt = $deletedAt WHERE Id = $id";
        command.Parameters.AddWithValue("$deletedAt", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public void Restore(long id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE FolderCase SET DeletedAt = NULL WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    /// <summary>Wipes the row. Only reachable from the bin, where the case is already out of sight.</summary>
    public void DeletePermanently(long id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM FolderCase WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Builds the ORDER BY from the closed <see cref="CaseSort"/> set — no caller text ever reaches
    /// the SQL. Id breaks ties so paging can't show a row twice or skip one, and every column falls
    /// back to the name so equal dates read alphabetically.
    /// </summary>
    private static string OrderBy(CaseFilter filter)
    {
        // Dates read newest-first by default (that is how the agenda is consulted); text reads A-Z.
        var (column, descendingByDefault) = filter.Sort switch
        {
            // FullNameSort, not FullName: SQLite compares bytes, so ÁLVARO and MUÑOZ would land
            // after Z on the raw text.
            CaseSort.Name => ("FullNameSort COLLATE NOCASE", false),
            CaseSort.Rut => ("Rut", false),
            CaseSort.Office => ("Office", false),
            CaseSort.FolderUploadedDate => ("FolderUploadedDate", true),
            CaseSort.LastFolderDate => ("LastFolderDate", false),
            CaseSort.FolderState => ("FolderState", false),
            CaseSort.FinalDecision => ("FinalDecision", false),
            _ => ("CitationDate", true)
        };

        var direction = filter.Descending != descendingByDefault ? "DESC" : "ASC";
        return $"{column} {direction}, FullNameSort COLLATE NOCASE ASC, Id ASC";
    }

    /// <summary>Accent-free, upper-cased copy of a name, for ordering only — never shown.</summary>
    private static string? SortKey(string? fullName)
        => fullName is null ? null : TextNormalizer.Normalize(fullName);

    private static string BuildWhere(CaseFilter filter, SqliteCommand command)
    {
        // Cases in the bin never show up in a listing, a count or an export — only in /Papelera.
        var clauses = new List<string> { "DeletedAt IS NULL" };

        if (filter.Office is { } office)
        {
            clauses.Add("Office = $office");
            command.Parameters.AddWithValue("$office", (int)office);
        }

        if (filter.Year is { } year)
        {
            clauses.Add("substr(CitationDate, 1, 4) = $year");
            command.Parameters.AddWithValue("$year", year.ToString("D4"));
        }

        if (filter.Month is { } month)
        {
            clauses.Add("substr(CitationDate, 6, 2) = $month");
            command.Parameters.AddWithValue("$month", month.ToString("D2"));
        }

        if (filter.FolderState is { } state)
        {
            clauses.Add("FolderState = $state");
            command.Parameters.AddWithValue("$state", (int)state);
        }

        if (filter.FinalDecision is { } decision)
        {
            clauses.Add("FinalDecision = $decision");
            command.Parameters.AddWithValue("$decision", (int)decision);
        }

        if (filter.Sector is { } sector)
        {
            var cutoff = new DateOnly(2023, 7, 1).ToString("yyyy-MM-dd");
            clauses.Add(sector == FolderSector.Archivo
                ? "(LastFolderDate IS NOT NULL AND LastFolderDate < $cutoff)"
                : "(LastFolderDate IS NOT NULL AND LastFolderDate >= $cutoff)");
            command.Parameters.AddWithValue("$cutoff", cutoff);
        }

        if (filter.OnlyNeedsReview)
        {
            clauses.Add("NeedsReview = 1");
        }

        if (filter.OnlyOtherComuna)
        {
            clauses.Add("LastFolderComuna IS NOT NULL");
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            // RUTs are searched with and without dots so "135516122" finds "13.551.612-2".
            clauses.Add("(FullName LIKE $search COLLATE NOCASE OR replace(replace(Rut, '.', ''), '-', '') LIKE $rutSearch)");
            command.Parameters.AddWithValue("$search", $"%{filter.Search.Trim()}%");
            command.Parameters.AddWithValue("$rutSearch",
                $"%{filter.Search.Trim().Replace(".", string.Empty).Replace("-", string.Empty)}%");
        }

        var builder = new StringBuilder("WHERE ");
        builder.AppendJoin(" AND ", clauses);
        return builder.ToString();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }

    private static string? Text(DateOnly? date) => date?.ToString("yyyy-MM-dd");

    private static object Nullable(string? value) => (object?)value ?? DBNull.Value;

    private static FolderCase Map(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(reader.GetOrdinal("Id")),
        CitationDate = ReadDate(reader, "CitationDate"),
        FolderUploadedDate = ReadDate(reader, "FolderUploadedDate"),
        LastFolderDate = ReadDate(reader, "LastFolderDate"),
        LastFolderComuna = ReadText(reader, "LastFolderComuna"),
        FirstName = ReadText(reader, "FirstName"),
        LastName = ReadText(reader, "LastName"),
        FullName = ReadText(reader, "FullName"),
        Rut = ReadText(reader, "Rut"),
        Office = (Office)reader.GetInt32(reader.GetOrdinal("Office")),
        AttentionNote = ReadText(reader, "AttentionNote"),
        Attended = reader.GetInt32(reader.GetOrdinal("Attended")) == 1,
        MoralIdoneity = ReadEnum<MoralIdoneity>(reader, "MoralIdoneity"),
        FolderState = ReadEnum<FolderState>(reader, "FolderState"),
        FolderStateRaw = ReadText(reader, "FolderStateRaw"),
        FinalDecision = ReadEnum<FinalDecision>(reader, "FinalDecision"),
        FinalDecisionRaw = ReadText(reader, "FinalDecisionRaw"),
        SourceSheet = ReadText(reader, "SourceSheet"),
        SourceRow = reader.GetInt32(reader.GetOrdinal("SourceRow")),
        NeedsReview = reader.GetInt32(reader.GetOrdinal("NeedsReview")) == 1,
        Marked = reader.GetInt32(reader.GetOrdinal("Marked")) == 1,
        CreatedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("CreatedAt"))),
        UpdatedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("UpdatedAt"))),
        DeletedAt = ReadText(reader, "DeletedAt") is { } deletedAt ? DateTimeOffset.Parse(deletedAt) : null,
        SectorPrintedAt = ReadText(reader, "SectorPrintedAt") is { } printedAt ? DateTimeOffset.Parse(printedAt) : null
    };

    private static string? ReadText(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static DateOnly? ReadDate(SqliteDataReader reader, string column)
    {
        var text = ReadText(reader, column);
        return text is null ? null : DateOnly.Parse(text);
    }

    private static TEnum? ReadEnum<TEnum>(SqliteDataReader reader, string column) where TEnum : struct, Enum
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : (TEnum)(object)reader.GetInt32(ordinal);
    }
}
