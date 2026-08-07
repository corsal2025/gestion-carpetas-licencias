using LicenciasCarpetas.Domain;
using Microsoft.Data.Sqlite;

namespace LicenciasCarpetas.Persistence;

public interface IComunaContactRepository
{
    void EnsureSchema();
    void Upsert(ComunaContact contact);
    IReadOnlyList<ComunaContact> All(string? search = null);
    void Delete(long id);
}

public sealed class ComunaContactRepository(string connectionString) : IComunaContactRepository
{
    public void EnsureSchema()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS ComunaContact (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Comuna TEXT NOT NULL,
                Email TEXT NOT NULL,
                Notes TEXT NULL,
                UNIQUE (Comuna, Email)
            );
            """;
        command.ExecuteNonQuery();
    }

    public void Upsert(ComunaContact contact)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ComunaContact (Comuna, Email, Notes)
            VALUES ($comuna, $email, $notes)
            ON CONFLICT(Comuna, Email) DO UPDATE SET Notes = COALESCE($notes, Notes)
            """;
        command.Parameters.AddWithValue("$comuna", contact.Comuna);
        command.Parameters.AddWithValue("$email", contact.Email);
        command.Parameters.AddWithValue("$notes", (object?)contact.Notes ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<ComunaContact> All(string? search = null)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        var where = string.IsNullOrWhiteSpace(search)
            ? string.Empty
            : "WHERE Comuna LIKE $search COLLATE NOCASE OR Email LIKE $search COLLATE NOCASE";
        command.CommandText = $"SELECT * FROM ComunaContact {where} ORDER BY Comuna ASC, Email ASC";
        if (!string.IsNullOrWhiteSpace(search))
        {
            command.Parameters.AddWithValue("$search", $"%{search.Trim()}%");
        }

        var results = new List<ComunaContact>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new ComunaContact
            {
                Id = reader.GetInt64(reader.GetOrdinal("Id")),
                Comuna = reader.GetString(reader.GetOrdinal("Comuna")),
                Email = reader.GetString(reader.GetOrdinal("Email")),
                Notes = reader.IsDBNull(reader.GetOrdinal("Notes")) ? null : reader.GetString(reader.GetOrdinal("Notes"))
            });
        }
        return results;
    }

    public void Delete(long id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ComunaContact WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }
}
