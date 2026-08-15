using LicenciasCarpetas.Domain;
using LicenciasCarpetas.Persistence;

namespace LicenciasCarpetas.Tests;

public class CaseSortingTests
{
    private static void Seed(SqliteTestDatabase db)
    {
        db.Cases.Insert(new FolderCase
        {
            FullName = "CARLA SOTO",
            Rut = "13.025.150-1",
            CitationDate = new DateOnly(2026, 1, 5),
            Office = Office.Placilla,
            LastFolderDate = new DateOnly(2019, 3, 1),
            FolderState = FolderState.SubidaConF8,
            SourceRow = 3
        });
        db.Cases.Insert(new FolderCase
        {
            FullName = "ANA PEREZ",
            Rut = "16.487.222-K",
            CitationDate = new DateOnly(2026, 1, 9),
            Office = Office.AvenidaArgentina,
            LastFolderDate = new DateOnly(2024, 6, 1),
            FolderState = FolderState.SubidaAConaset,
            SourceRow = 4
        });
        db.Cases.Insert(new FolderCase
        {
            FullName = "BRUNO DIAZ",
            Rut = "5.667.048-3",
            CitationDate = new DateOnly(2026, 1, 2),
            Office = Office.MercadoPuerto,
            LastFolderDate = new DateOnly(2021, 12, 1),
            FolderState = FolderState.PrimeraLicencia,
            SourceRow = 5
        });
    }

    private static string[] Names(IReadOnlyList<FolderCase> cases) => [.. cases.Select(c => c.FullName!)];

    [Fact]
    public void Sorts_by_name_ascending()
    {
        using var db = new SqliteTestDatabase();
        Seed(db);

        var sorted = db.Cases.Query(new CaseFilter { Sort = CaseSort.Name }, 0, 50);

        Assert.Equal(["ANA PEREZ", "BRUNO DIAZ", "CARLA SOTO"], Names(sorted));
    }

    [Fact]
    public void Sorts_by_name_descending()
    {
        using var db = new SqliteTestDatabase();
        Seed(db);

        var sorted = db.Cases.Query(new CaseFilter { Sort = CaseSort.Name, Descending = true }, 0, 50);

        Assert.Equal(["CARLA SOTO", "BRUNO DIAZ", "ANA PEREZ"], Names(sorted));
    }

    [Fact]
    public void Sorts_by_citation_date_newest_first_by_default()
    {
        using var db = new SqliteTestDatabase();
        Seed(db);

        var sorted = db.Cases.Query(new CaseFilter(), 0, 50);

        Assert.Equal(["ANA PEREZ", "CARLA SOTO", "BRUNO DIAZ"], Names(sorted));
    }

    [Fact]
    public void Sorts_by_last_folder_date_oldest_first()
    {
        using var db = new SqliteTestDatabase();
        Seed(db);

        var sorted = db.Cases.Query(new CaseFilter { Sort = CaseSort.LastFolderDate }, 0, 50);

        Assert.Equal(["CARLA SOTO", "BRUNO DIAZ", "ANA PEREZ"], Names(sorted));
    }

    [Fact]
    public void Sorts_by_office()
    {
        using var db = new SqliteTestDatabase();
        Seed(db);

        var sorted = db.Cases.Query(new CaseFilter { Sort = CaseSort.Office }, 0, 50);

        Assert.Equal([Office.AvenidaArgentina, Office.Placilla, Office.MercadoPuerto],
            sorted.Select(c => c.Office).ToArray());
    }

    /// <summary>Paging has to walk the same ordering, or a row shows twice and another never shows.</summary>
    [Fact]
    public void The_ordering_stays_consistent_across_pages()
    {
        using var db = new SqliteTestDatabase();
        Seed(db);
        var filter = new CaseFilter { Sort = CaseSort.Name };

        var first = db.Cases.Query(filter, 0, 2);
        var second = db.Cases.Query(filter, 2, 2);

        Assert.Equal(["ANA PEREZ", "BRUNO DIAZ"], Names(first));
        Assert.Equal(["CARLA SOTO"], Names(second));
    }

    [Fact]
    public void The_export_uses_the_same_ordering_as_the_screen()
    {
        using var db = new SqliteTestDatabase();
        Seed(db);
        var filter = new CaseFilter { Sort = CaseSort.Rut, Descending = true };

        Assert.Equal(
            Names(db.Cases.Query(filter, 0, 50)),
            Names(db.Cases.QueryAll(filter)));
    }
}
