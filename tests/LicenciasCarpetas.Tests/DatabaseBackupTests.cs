using LicenciasCarpetas.Persistence;

namespace LicenciasCarpetas.Tests;

/// <summary>
/// Every case, every edit and every user live in a single 8 MB file. Losing it means retyping a
/// year of agenda, so a copy is taken on each start.
/// </summary>
public class DatabaseBackupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"carpetas-backup-{Guid.NewGuid():N}");

    public DatabaseBackupTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string WriteDatabase(string content = "datos-de-prueba")
    {
        var path = Path.Combine(_root, "carpetas.db");
        File.WriteAllText(path, content);
        return path;
    }

    private string BackupDirectory => Path.Combine(_root, "backups");

    private string[] Backups() => Directory.Exists(BackupDirectory)
        ? [.. Directory.GetFiles(BackupDirectory).Order()]
        : [];

    [Fact]
    public void Copies_the_database_into_the_backup_folder()
    {
        var databasePath = WriteDatabase();
        var backup = new DatabaseBackup(databasePath, BackupDirectory, keep: 5);

        var created = backup.Run(new DateTimeOffset(2026, 8, 14, 9, 30, 0, TimeSpan.Zero));

        Assert.NotNull(created);
        Assert.True(File.Exists(created));
        Assert.Equal("datos-de-prueba", File.ReadAllText(created));
        Assert.Contains("20260814-0930", Path.GetFileName(created));
    }

    [Fact]
    public void Keeps_only_the_newest_copies()
    {
        var databasePath = WriteDatabase();
        var backup = new DatabaseBackup(databasePath, BackupDirectory, keep: 3);

        for (var day = 1; day <= 5; day++)
        {
            backup.Run(new DateTimeOffset(2026, 8, day, 9, 0, 0, TimeSpan.Zero));
        }

        var remaining = Backups();
        Assert.Equal(3, remaining.Length);
        // The three most recent runs survive; the first two are gone.
        Assert.Contains("20260803", string.Join(' ', remaining));
        Assert.Contains("20260805", string.Join(' ', remaining));
        Assert.DoesNotContain("20260801", string.Join(' ', remaining));
    }

    [Fact]
    public void Two_runs_in_the_same_minute_do_not_collide()
    {
        var databasePath = WriteDatabase();
        var backup = new DatabaseBackup(databasePath, BackupDirectory, keep: 5);
        var moment = new DateTimeOffset(2026, 8, 14, 9, 30, 0, TimeSpan.Zero);

        var first = backup.Run(moment);
        var second = backup.Run(moment);

        Assert.Equal(first, second);
        Assert.Single(Backups());
    }

    [Fact]
    public void Does_nothing_when_there_is_no_database_yet()
    {
        var backup = new DatabaseBackup(Path.Combine(_root, "no-existe.db"), BackupDirectory, keep: 5);

        Assert.Null(backup.Run(DateTimeOffset.UtcNow));
        Assert.Empty(Backups());
    }

    /// <summary>A backup that throws must never stop the operator from working.</summary>
    [Fact]
    public void A_failure_is_reported_as_no_backup_rather_than_thrown()
    {
        var databasePath = WriteDatabase();
        // A file where the backup folder should go: creating the directory cannot succeed.
        File.WriteAllText(BackupDirectory, "no soy una carpeta");
        var backup = new DatabaseBackup(databasePath, BackupDirectory, keep: 5);

        Assert.Null(backup.Run(DateTimeOffset.UtcNow));
    }
}
