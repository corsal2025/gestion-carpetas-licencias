using LicenciasCarpetas.Domain;
using LicenciasCarpetas.Persistence;

namespace LicenciasCarpetas.Tests;

/// <summary>
/// The sector list is what the operator walks to Archivo or Oficina 43 with. Reprinting it used to
/// repeat everyone already requested, so the same folders got asked for twice.
/// </summary>
public class SectorPrintingTests
{
    private static long Insert(SqliteTestDatabase db, string name, string rut, DateOnly lastFolder, bool marked = true)
    {
        var id = db.Cases.Insert(new FolderCase
        {
            FullName = name,
            Rut = rut,
            CitationDate = new DateOnly(2026, 1, 2),
            Office = Office.AvenidaArgentina,
            LastFolderDate = lastFolder,
            Marked = marked
        });
        return id;
    }

    private static readonly DateOnly ArchivoDate = new(2020, 5, 1);

    [Fact]
    public void A_case_already_requested_drops_off_the_next_list()
    {
        using var db = new SqliteTestDatabase();
        var first = Insert(db, "ANA PEREZ", "13.025.150-1", ArchivoDate);
        Insert(db, "BRUNO DIAZ", "16.487.222-K", ArchivoDate);

        db.Cases.MarkSectorPrinted([first]);

        var pending = db.Cases.ForSector(FolderSector.Archivo, onlyMarked: true, includePrinted: false);
        Assert.Equal("BRUNO DIAZ", Assert.Single(pending).FullName);
    }

    [Fact]
    public void The_already_requested_ones_can_still_be_listed_on_purpose()
    {
        using var db = new SqliteTestDatabase();
        var first = Insert(db, "ANA PEREZ", "13.025.150-1", ArchivoDate);
        Insert(db, "BRUNO DIAZ", "16.487.222-K", ArchivoDate);
        db.Cases.MarkSectorPrinted([first]);

        var all = db.Cases.ForSector(FolderSector.Archivo, onlyMarked: true, includePrinted: true);

        Assert.Equal(2, all.Count);
        Assert.Single(all, item => item.SectorPrintedAt is not null);
    }

    [Fact]
    public void Requesting_a_folder_again_puts_it_back_on_the_list()
    {
        using var db = new SqliteTestDatabase();
        var id = Insert(db, "ANA PEREZ", "13.025.150-1", ArchivoDate);
        db.Cases.MarkSectorPrinted([id]);

        db.Cases.ClearSectorPrinted(id);

        Assert.Single(db.Cases.ForSector(FolderSector.Archivo, onlyMarked: true, includePrinted: false));
        Assert.Null(db.Cases.FindById(id)!.SectorPrintedAt);
    }

    [Fact]
    public void Marking_the_list_records_when_it_was_requested()
    {
        using var db = new SqliteTestDatabase();
        var id = Insert(db, "ANA PEREZ", "13.025.150-1", ArchivoDate);

        db.Cases.MarkSectorPrinted([id]);

        var stored = db.Cases.FindById(id)!;
        Assert.NotNull(stored.SectorPrintedAt);
        Assert.True(DateTimeOffset.UtcNow - stored.SectorPrintedAt < TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void Marking_an_empty_list_changes_nothing()
    {
        using var db = new SqliteTestDatabase();
        Insert(db, "ANA PEREZ", "13.025.150-1", ArchivoDate);

        db.Cases.MarkSectorPrinted([]);

        Assert.Single(db.Cases.ForSector(FolderSector.Archivo, onlyMarked: true, includePrinted: false));
    }

    /// <summary>Marking one sector's list must not silently clear the other sector's pending work.</summary>
    [Fact]
    public void Only_the_listed_cases_are_marked()
    {
        using var db = new SqliteTestDatabase();
        var archivo = Insert(db, "ANA PEREZ", "13.025.150-1", ArchivoDate);
        Insert(db, "BRUNO DIAZ", "16.487.222-K", new DateOnly(2024, 1, 1));

        db.Cases.MarkSectorPrinted([archivo]);

        Assert.Single(db.Cases.ForSector(FolderSector.Oficina43, onlyMarked: true, includePrinted: false));
        Assert.Empty(db.Cases.ForSector(FolderSector.Archivo, onlyMarked: true, includePrinted: false));
    }

    /// <summary>Re-importing the workbook must not wipe what has already been requested.</summary>
    [Fact]
    public void An_import_does_not_clear_the_requested_mark()
    {
        using var db = new SqliteTestDatabase();
        var folderCase = new FolderCase
        {
            FullName = "ANA PEREZ",
            Rut = "13.025.150-1",
            CitationDate = new DateOnly(2026, 1, 2),
            Office = Office.AvenidaArgentina,
            LastFolderDate = ArchivoDate,
            Marked = true,
            SourceSheet = "ENERO AV. ARGENTINA",
            SourceRow = 3
        };
        var id = db.Cases.Insert(folderCase);
        db.Cases.MarkSectorPrinted([id]);

        db.Cases.Upsert(folderCase);

        Assert.NotNull(db.Cases.FindById(id)!.SectorPrintedAt);
    }
}
