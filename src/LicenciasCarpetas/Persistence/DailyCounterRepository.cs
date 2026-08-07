using LicenciasCarpetas.Domain;
using Microsoft.Data.Sqlite;

namespace LicenciasCarpetas.Persistence;

public interface IDailyCounterRepository
{
    void EnsureSchema();
    void Upsert(DailyCounter counter);
    DailyCounter? Find(DateOnly date);
    IReadOnlyList<DailyCounter> ForMonth(int year, int month);
}

public sealed class DailyCounterRepository(string connectionString) : IDailyCounterRepository
{
    public void EnsureSchema()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS DailyCounter (
                Date TEXT PRIMARY KEY,
                Scanned INTEGER NULL,
                Uploaded INTEGER NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// A null value leaves whatever is already stored alone: the workbook has months where only one
    /// of the two numbers was filled in, and an import must not blank the other one.
    /// </summary>
    public void Upsert(DailyCounter counter)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO DailyCounter (Date, Scanned, Uploaded)
            VALUES ($date, $scanned, $uploaded)
            ON CONFLICT(Date) DO UPDATE SET
                Scanned = COALESCE($scanned, Scanned),
                Uploaded = COALESCE($uploaded, Uploaded)
            """;
        command.Parameters.AddWithValue("$date", counter.Date.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$scanned", (object?)counter.Scanned ?? DBNull.Value);
        command.Parameters.AddWithValue("$uploaded", (object?)counter.Uploaded ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    public DailyCounter? Find(DateOnly date)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM DailyCounter WHERE Date = $date LIMIT 1";
        command.Parameters.AddWithValue("$date", date.ToString("yyyy-MM-dd"));
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public IReadOnlyList<DailyCounter> ForMonth(int year, int month)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT * FROM DailyCounter
            WHERE substr(Date, 1, 4) = $year AND substr(Date, 6, 2) = $month
            ORDER BY Date ASC
            """;
        command.Parameters.AddWithValue("$year", year.ToString("D4"));
        command.Parameters.AddWithValue("$month", month.ToString("D2"));

        var results = new List<DailyCounter>();
        using var reader = command.ExecuteReader();
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
        return connection;
    }

    private static DailyCounter Map(SqliteDataReader reader) => new()
    {
        Date = DateOnly.Parse(reader.GetString(reader.GetOrdinal("Date"))),
        Scanned = ReadInt(reader, "Scanned"),
        Uploaded = ReadInt(reader, "Uploaded")
    };

    private static int? ReadInt(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : Convert.ToInt32(reader.GetValue(ordinal));
    }
}
