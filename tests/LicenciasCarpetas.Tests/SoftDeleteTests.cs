using LicenciasCarpetas.Domain;
using LicenciasCarpetas.Persistence;

namespace LicenciasCarpetas.Tests;

/// <summary>
/// Deleting a case used to be irreversible. These rows are the only record of a citation once the
/// workbook stops being maintained, so a delete has to be undoable.
/// </summary>
public class SoftDeleteTests
{
    private static FolderCase Case(string rut = "13.025.150-1", string name = "JUAN PEREZ", int row = 3)
        => new()
        {
            FullName = name,
            Rut = rut,
            CitationDate = new DateOnly(2026, 1, 2),
            Office = Office.AvenidaArgentina,
            Attended = true,
            LastFolderDate = new DateOnly(2020, 1, 1),
            SourceSheet = "ENERO AV. ARGENTINA",
            SourceRow = row
        };

    [Fact]
    public void A_deleted_case_disappears_from_the_normal_listing_but_is_kept()
    {
        using var db = new SqliteTestDatabase();
        var id = db.Cases.Insert(Case());

        db.Cases.Delete(id);

        Assert.Equal(0, db.Cases.Count(new CaseFilter()));
        Assert.Empty(db.Cases.Query(new CaseFilter(), 0, 50));
        Assert.Single(db.Cases.Deleted());
    }

    [Fact]
    public void A_deleted_case_can_be_restored()
    {
        using var db = new SqliteTestDatabase();
        var id = db.Cases.Insert(Case());
        db.Cases.Delete(id);

        db.Cases.Restore(id);

        Assert.Equal(1, db.Cases.Count(new CaseFilter()));
        Assert.Empty(db.Cases.Deleted());
    }

    [Fact]
    public void Emptying_the_bin_removes_the_row_for_good()
    {
        using var db = new SqliteTestDatabase();
        var id = db.Cases.Insert(Case());
        db.Cases.Delete(id);

        db.Cases.DeletePermanently(id);

        Assert.Empty(db.Cases.Deleted());
        Assert.Null(db.Cases.FindById(id));
    }

    [Fact]
    public void Deleted_cases_are_left_out_of_the_sector_lists()
    {
        using var db = new SqliteTestDatabase();
        var id = db.Cases.Insert(Case());
        db.Cases.Insert(Case(rut: "16.487.222-K", name: "MARGARITA PARRAGUEZ", row: 4));

        db.Cases.Delete(id);

        Assert.Single(db.Cases.ForSector(FolderSector.Archivo, onlyMarked: false));
    }

    [Fact]
    public void Deleted_cases_are_left_out_of_the_statistics()
    {
        using var db = new SqliteTestDatabase();
        var id = db.Cases.Insert(Case());
        db.Cases.Insert(Case(rut: "16.487.222-K", name: "MARGARITA PARRAGUEZ", row: 4));

        db.Cases.Delete(id);

        var attendance = db.Cases.DailyAttendance(2026, 1).Single();
        Assert.Equal(1, attendance.Scheduled);
        Assert.Equal(1, attendance.Attended);
        Assert.Equal(1, db.Cases.FolderStateBreakdown(2026, 1, office: null).Sum(entry => entry.Count));
    }

    [Fact]
    public void Deleted_cases_are_left_out_of_the_review_count()
    {
        using var db = new SqliteTestDatabase();
        var folderCase = Case();
        folderCase.NeedsReview = true;
        var id = db.Cases.Insert(folderCase);

        db.Cases.Delete(id);

        Assert.Equal(0, db.Cases.CountNeedingReview());
    }

    /// <summary>
    /// Re-importing the workbook updates the row it matches, but must not silently bring back a case
    /// the operator deleted on purpose — the workbook keeps rows the operator already discarded.
    /// </summary>
    [Fact]
    public void Re_importing_does_not_resurrect_a_deleted_case_nor_duplicate_it()
    {
        using var db = new SqliteTestDatabase();
        var id = db.Cases.Insert(Case());
        db.Cases.Delete(id);

        var outcome = db.Cases.Upsert(Case());

        Assert.Equal(UpsertOutcome.Updated, outcome);
        Assert.Equal(0, db.Cases.Count(new CaseFilter()));
        Assert.Single(db.Cases.Deleted());
    }
}
