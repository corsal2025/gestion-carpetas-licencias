namespace LicenciasCarpetas.Persistence;

/// <summary>
/// Copies the SQLite file on every start and keeps the last few copies. The whole department's
/// agenda — cases, edits and users — is that one file; a copy costs a few megabytes and a moment,
/// and losing it costs a year of retyping.
/// </summary>
public sealed class DatabaseBackup(string databasePath, string backupDirectory, int keep)
{
    /// <summary>
    /// Returns the path of the copy, or null when there was nothing to copy or the copy failed —
    /// a backup problem is never a reason to stop the operator from working.
    /// </summary>
    public string? Run(DateTimeOffset now)
    {
        try
        {
            if (!File.Exists(databasePath))
            {
                return null;
            }

            Directory.CreateDirectory(backupDirectory);

            var name = $"{Path.GetFileNameWithoutExtension(databasePath)}-{now:yyyyMMdd-HHmm}.db";
            var destination = Path.Combine(backupDirectory, name);

            // Restarting the app twice within the same minute must not pile up identical copies.
            if (!File.Exists(destination))
            {
                File.Copy(databasePath, destination);
            }

            RemoveOldCopies();
            return destination;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void RemoveOldCopies()
    {
        var copies = Directory.GetFiles(backupDirectory, "*.db")
            .OrderByDescending(path => path, StringComparer.Ordinal) // the timestamp sorts as text
            .Skip(Math.Max(keep, 1))
            .ToList();

        foreach (var old in copies)
        {
            try
            {
                File.Delete(old);
            }
            catch (IOException)
            {
                // Locked or already gone: the next start will try again.
            }
        }
    }
}
