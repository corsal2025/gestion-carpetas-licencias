using Microsoft.Data.Sqlite;

namespace LicenciasCarpetas.Dashboard.Auth;

public interface IUserRepository
{
    void EnsureSchema();
    DashboardUser? FindByUsername(string username);
    DashboardUser? FindById(long id);
    void Insert(DashboardUser user);
    void UpdatePassword(long id, string hash, string salt, int iterations);

    /// <summary>Recovery: sets a new password and clears any lockout, so the operator can get in
    /// straight away instead of waiting out the fifteen minutes that locked them out.</summary>
    void ResetPassword(long id, string hash, string salt, int iterations);

    /// <summary>Usernames, for the operator who forgot which account they created.</summary>
    IReadOnlyList<string> AllUsernames();
    void Delete(string username);
    void RecordFailedLogin(long id, int attempts, DateTimeOffset? lockedUntil);
    void ResetFailedLogins(long id);
    int Count();
}

public sealed class UserRepository(string connectionString) : IUserRepository
{
    public void EnsureSchema()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS DashboardUser (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Username TEXT NOT NULL UNIQUE,
                PasswordHash TEXT NOT NULL,
                PasswordSalt TEXT NOT NULL,
                Iterations INTEGER NOT NULL,
                FailedLoginAttempts INTEGER NOT NULL DEFAULT 0,
                LockedUntil TEXT NULL,
                CreatedAt TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    public DashboardUser? FindByUsername(string username)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        // COLLATE NOCASE and a trim: accounts are stored lower-cased, but the operator types the
        // name however they type it, and SQLite compares text case-sensitively by default.
        command.CommandText = "SELECT * FROM DashboardUser WHERE Username = $username COLLATE NOCASE LIMIT 1";
        command.Parameters.AddWithValue("$username", username.Trim());
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public DashboardUser? FindById(long id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM DashboardUser WHERE Id = $id LIMIT 1";
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public void Insert(DashboardUser user)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO DashboardUser (Username, PasswordHash, PasswordSalt, Iterations, FailedLoginAttempts, LockedUntil, CreatedAt)
            VALUES ($username, $hash, $salt, $iterations, 0, NULL, $createdAt)
            """;
        command.Parameters.AddWithValue("$username", user.Username);
        command.Parameters.AddWithValue("$hash", user.PasswordHash);
        command.Parameters.AddWithValue("$salt", user.PasswordSalt);
        command.Parameters.AddWithValue("$iterations", user.Iterations);
        command.Parameters.AddWithValue("$createdAt", user.CreatedAt.ToString("O"));
        command.ExecuteNonQuery();
    }

    public void UpdatePassword(long id, string hash, string salt, int iterations)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE DashboardUser SET PasswordHash = $hash, PasswordSalt = $salt, Iterations = $iterations WHERE Id = $id";
        command.Parameters.AddWithValue("$hash", hash);
        command.Parameters.AddWithValue("$salt", salt);
        command.Parameters.AddWithValue("$iterations", iterations);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public void ResetPassword(long id, string hash, string salt, int iterations)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE DashboardUser
            SET PasswordHash = $hash, PasswordSalt = $salt, Iterations = $iterations,
                FailedLoginAttempts = 0, LockedUntil = NULL
            WHERE Id = $id
            """;
        command.Parameters.AddWithValue("$hash", hash);
        command.Parameters.AddWithValue("$salt", salt);
        command.Parameters.AddWithValue("$iterations", iterations);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public void Delete(string username)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM DashboardUser WHERE Username = $username";
        command.Parameters.AddWithValue("$username", username);
        command.ExecuteNonQuery();
    }

    public void RecordFailedLogin(long id, int attempts, DateTimeOffset? lockedUntil)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE DashboardUser SET FailedLoginAttempts = $attempts, LockedUntil = $lockedUntil WHERE Id = $id";
        command.Parameters.AddWithValue("$attempts", attempts);
        command.Parameters.AddWithValue("$lockedUntil", (object?)lockedUntil?.ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public void ResetFailedLogins(long id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE DashboardUser SET FailedLoginAttempts = 0, LockedUntil = NULL WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<string> AllUsernames()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Username FROM DashboardUser ORDER BY Username ASC";

        var names = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }
        return names;
    }

    public int Count()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM DashboardUser";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }

    private static DashboardUser Map(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(reader.GetOrdinal("Id")),
        Username = reader.GetString(reader.GetOrdinal("Username")),
        PasswordHash = reader.GetString(reader.GetOrdinal("PasswordHash")),
        PasswordSalt = reader.GetString(reader.GetOrdinal("PasswordSalt")),
        Iterations = reader.GetInt32(reader.GetOrdinal("Iterations")),
        FailedLoginAttempts = reader.GetInt32(reader.GetOrdinal("FailedLoginAttempts")),
        LockedUntil = reader.IsDBNull(reader.GetOrdinal("LockedUntil"))
            ? null
            : DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("LockedUntil"))),
        CreatedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("CreatedAt")))
    };
}
