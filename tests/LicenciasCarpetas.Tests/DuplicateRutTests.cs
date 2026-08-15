using LicenciasCarpetas.Domain;
using LicenciasCarpetas.Persistence;

namespace LicenciasCarpetas.Tests;

/// <summary>
/// The workbook flags a repeated RUT in violet with a COUNTIF — the same person cited twice in the
/// same agenda, which is normally a mistake worth seeing before the folder is requested twice.
/// </summary>
public class DuplicateRutTests
{
    private static void Insert(SqliteTestDatabase db, string rut, int day, Office office = Office.AvenidaArgentina, int row = 3)
        => db.Cases.Insert(new FolderCase
        {
            FullName = $"PERSONA {rut}",
            Rut = rut,
            CitationDate = new DateOnly(2026, 1, day),
            Office = office,
            SourceSheet = "ENERO AV. ARGENTINA",
            SourceRow = row
        });

    [Fact]
    public void Finds_the_rut_that_appears_more_than_once()
    {
        using var db = new SqliteTestDatabase();
        Insert(db, "13.025.150-1", 2, row: 3);
        Insert(db, "13.025.150-1", 9, row: 4);
        Insert(db, "16.487.222-K", 2, row: 5);

        var duplicates = db.Cases.DuplicateRuts(new CaseFilter());

        Assert.Equal(["13.025.150-1"], duplicates);
    }

    [Fact]
    public void A_rut_seen_once_is_not_a_duplicate()
    {
        using var db = new SqliteTestDatabase();
        Insert(db, "13.025.150-1", 2);

        Assert.Empty(db.Cases.DuplicateRuts(new CaseFilter()));
    }

    /// <summary>The workbook counts within one sheet — one month and one office. The equivalent
    /// here is whatever the operator is filtering by.</summary>
    [Fact]
    public void Duplicates_are_counted_inside_the_current_filter()
    {
        using var db = new SqliteTestDatabase();
        Insert(db, "13.025.150-1", 2, Office.AvenidaArgentina, row: 3);
        Insert(db, "13.025.150-1", 2, Office.Placilla, row: 4);

        Assert.Empty(db.Cases.DuplicateRuts(new CaseFilter { Office = Office.AvenidaArgentina }));
        Assert.Single(db.Cases.DuplicateRuts(new CaseFilter()));
    }

    [Fact]
    public void Cases_in_the_bin_do_not_count_as_duplicates()
    {
        using var db = new SqliteTestDatabase();
        Insert(db, "13.025.150-1", 2, row: 3);
        Insert(db, "13.025.150-1", 9, row: 4);
        var id = db.Cases.Query(new CaseFilter(), 0, 10)[0].Id;

        db.Cases.Delete(id);

        Assert.Empty(db.Cases.DuplicateRuts(new CaseFilter()));
    }

    [Fact]
    public void Rows_without_a_rut_are_not_reported_as_duplicates_of_each_other()
    {
        using var db = new SqliteTestDatabase();
        db.Cases.Insert(new FolderCase { FullName = "SIN RUT UNO", Office = Office.AvenidaArgentina, CitationDate = new DateOnly(2026, 1, 2) });
        db.Cases.Insert(new FolderCase { FullName = "SIN RUT DOS", Office = Office.AvenidaArgentina, CitationDate = new DateOnly(2026, 1, 2) });

        Assert.Empty(db.Cases.DuplicateRuts(new CaseFilter()));
    }
}
