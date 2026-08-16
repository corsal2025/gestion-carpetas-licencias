using LicenciasCarpetas.Domain;
using LicenciasCarpetas.Persistence;

namespace LicenciasCarpetas.Tests;

/// <summary>
/// Two fields the workbook never had: the F8 case code and the date of the folder before the last
/// one. Both are typed by hand and both have to survive a re-import of the workbook, which knows
/// nothing about them.
/// </summary>
public class F8AndPreviousFolderTests
{
    private static FolderCase Case(int row = 3) => new()
    {
        FullName = "JUAN PEREZ",
        Rut = "13.025.150-1",
        CitationDate = new DateOnly(2026, 1, 2),
        Office = Office.AvenidaArgentina,
        SourceSheet = "ENERO AV. ARGENTINA",
        SourceRow = row
    };

    [Fact]
    public void Both_fields_are_stored_and_read_back()
    {
        using var db = new SqliteTestDatabase();
        var folderCase = Case();
        folderCase.CodigoF8 = "F8-2026-114";
        folderCase.PenultimateFolderDate = new DateOnly(2015, 4, 1);

        var id = db.Cases.Insert(folderCase);

        var stored = db.Cases.FindById(id)!;
        Assert.Equal("F8-2026-114", stored.CodigoF8);
        Assert.Equal(new DateOnly(2015, 4, 1), stored.PenultimateFolderDate);
    }

    [Fact]
    public void Both_fields_are_editable_from_the_cases_screen()
    {
        using var db = new SqliteTestDatabase();
        var id = db.Cases.Insert(Case());

        db.Cases.UpdateCaseDetails(id, "F8-2026-115", new DateOnly(2016, 9, 1));

        var stored = db.Cases.FindById(id)!;
        Assert.Equal("F8-2026-115", stored.CodigoF8);
        Assert.Equal(new DateOnly(2016, 9, 1), stored.PenultimateFolderDate);
    }

    [Fact]
    public void Both_fields_can_be_cleared()
    {
        using var db = new SqliteTestDatabase();
        var folderCase = Case();
        folderCase.CodigoF8 = "F8-2026-114";
        folderCase.PenultimateFolderDate = new DateOnly(2015, 4, 1);
        var id = db.Cases.Insert(folderCase);

        db.Cases.UpdateCaseDetails(id, null, null);

        var stored = db.Cases.FindById(id)!;
        Assert.Null(stored.CodigoF8);
        Assert.Null(stored.PenultimateFolderDate);
    }

    /// <summary>
    /// The workbook has no column for either, so an import must not blank what the operator typed —
    /// the same rule that already protects their personal "marcar" tick.
    /// </summary>
    [Fact]
    public void A_re_import_does_not_erase_what_the_operator_typed()
    {
        using var db = new SqliteTestDatabase();
        var id = db.Cases.Insert(Case());
        db.Cases.UpdateCaseDetails(id, "F8-2026-114", new DateOnly(2015, 4, 1));

        db.Cases.Upsert(Case());

        var stored = db.Cases.FindById(id)!;
        Assert.Equal("F8-2026-114", stored.CodigoF8);
        Assert.Equal(new DateOnly(2015, 4, 1), stored.PenultimateFolderDate);
    }

    [Fact]
    public void The_case_list_carries_both_fields()
    {
        using var db = new SqliteTestDatabase();
        var id = db.Cases.Insert(Case());
        db.Cases.UpdateCaseDetails(id, "F8-2026-114", new DateOnly(2015, 4, 1));

        var listed = Assert.Single(db.Cases.Query(new CaseFilter(), 0, 10));
        Assert.Equal("F8-2026-114", listed.CodigoF8);
        Assert.Equal(new DateOnly(2015, 4, 1), listed.PenultimateFolderDate);
    }

    /// <summary>The penúltima carpeta never decides the sector — that stays with the última.</summary>
    [Fact]
    public void The_sector_still_comes_from_the_last_folder_only()
    {
        var folderCase = Case();
        folderCase.LastFolderDate = new DateOnly(2024, 1, 1);
        folderCase.PenultimateFolderDate = new DateOnly(2010, 1, 1);

        Assert.Equal(FolderSector.Oficina43, folderCase.Sector);
    }
}
