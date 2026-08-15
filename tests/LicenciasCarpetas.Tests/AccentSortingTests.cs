using LicenciasCarpetas.Domain;
using LicenciasCarpetas.Persistence;

namespace LicenciasCarpetas.Tests;

/// <summary>
/// SQLite compares text byte by byte, so "ÁLVARO" lands after "Z" and "MUÑOZ" after "MUZ" — with
/// Chilean surnames that is most of the list in the wrong place. Ordering runs on an accent-free
/// copy of the name kept alongside it.
/// </summary>
public class AccentSortingTests
{
    private static void Insert(SqliteTestDatabase db, string name, int row)
        => db.Cases.Insert(new FolderCase
        {
            FullName = name,
            CitationDate = new DateOnly(2026, 1, 2),
            Office = Office.AvenidaArgentina,
            SourceSheet = "ENERO AV. ARGENTINA",
            SourceRow = row
        });

    private static string[] SortedNames(SqliteTestDatabase db)
        => [.. db.Cases.Query(new CaseFilter { Sort = CaseSort.Name }, 0, 50).Select(c => c.FullName!)];

    [Fact]
    public void An_accented_name_sorts_where_its_letter_belongs()
    {
        using var db = new SqliteTestDatabase();
        Insert(db, "ZUNILDA SOLORZA", 3);
        Insert(db, "ÁLVARO VARGAS", 4);
        Insert(db, "BRUNO DIAZ", 5);

        Assert.Equal(["ÁLVARO VARGAS", "BRUNO DIAZ", "ZUNILDA SOLORZA"], SortedNames(db));
    }

    [Fact]
    public void The_enye_sorts_as_an_n()
    {
        using var db = new SqliteTestDatabase();
        Insert(db, "MUZA PEREZ", 3);
        Insert(db, "MUÑOZ SOTO", 4);
        Insert(db, "MUNITA LARA", 5);

        Assert.Equal(["MUNITA LARA", "MUÑOZ SOTO", "MUZA PEREZ"], SortedNames(db));
    }

    [Fact]
    public void Accents_do_not_change_the_order_between_otherwise_equal_names()
    {
        using var db = new SqliteTestDatabase();
        Insert(db, "PEREZ ANA", 3);
        Insert(db, "PÉREZ BEATRIZ", 4);

        Assert.Equal(["PEREZ ANA", "PÉREZ BEATRIZ"], SortedNames(db));
    }

    [Fact]
    public void Descending_reverses_the_same_accent_free_order()
    {
        using var db = new SqliteTestDatabase();
        Insert(db, "ZUNILDA SOLORZA", 3);
        Insert(db, "ÁLVARO VARGAS", 4);

        var sorted = db.Cases.Query(new CaseFilter { Sort = CaseSort.Name, Descending = true }, 0, 50);

        Assert.Equal(["ZUNILDA SOLORZA", "ÁLVARO VARGAS"], sorted.Select(c => c.FullName!).ToArray());
    }

    /// <summary>
    /// Databases imported before this existed have no sort key. EnsureSchema has to fill it in, or
    /// every case imported so far keeps sorting the old broken way.
    /// </summary>
    [Fact]
    public void Cases_stored_before_the_sort_key_existed_are_backfilled()
    {
        using var db = new SqliteTestDatabase();
        Insert(db, "ÁLVARO VARGAS", 3);
        Insert(db, "BRUNO DIAZ", 4);

        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(db.ConnectionString))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE FolderCase SET FullNameSort = NULL";
            command.ExecuteNonQuery();
        }

        db.Cases.EnsureSchema();

        Assert.Equal(["ÁLVARO VARGAS", "BRUNO DIAZ"], SortedNames(db));
    }

    [Fact]
    public void Editing_a_name_updates_its_sort_key()
    {
        using var db = new SqliteTestDatabase();
        Insert(db, "ZULEMA ROJAS", 3);
        Insert(db, "BRUNO DIAZ", 4);
        var id = db.Cases.Query(new CaseFilter { Sort = CaseSort.Name }, 0, 50).Last().Id;

        db.Cases.UpdateEditableFields(id, "ÁNGELA MUÑOZ", null, new DateOnly(2026, 1, 2),
            null, null, null, null, null, null, null, needsReview: false);

        Assert.Equal(["ÁNGELA MUÑOZ", "BRUNO DIAZ"], SortedNames(db));
    }
}
