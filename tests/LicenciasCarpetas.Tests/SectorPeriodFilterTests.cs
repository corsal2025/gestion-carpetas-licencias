using LicenciasCarpetas.Domain;
using LicenciasCarpetas.Persistence;

namespace LicenciasCarpetas.Tests;

/// <summary>
/// The folder request is prepared for a day's agenda ("las carpetas de los citados del martes") or
/// for a whole month, so the sector list can be narrowed by citation date.
/// </summary>
public class SectorPeriodFilterTests
{
    private static readonly DateOnly ArchivoDate = new(2020, 5, 1);

    private static void Insert(SqliteTestDatabase db, string name, string rut, DateOnly citation)
        => db.Cases.Insert(new FolderCase
        {
            FullName = name,
            Rut = rut,
            CitationDate = citation,
            Office = Office.AvenidaArgentina,
            LastFolderDate = ArchivoDate,
            Marked = true
        });

    private static SqliteTestDatabase Seeded()
    {
        var db = new SqliteTestDatabase();
        Insert(db, "CITADO 2 ENERO", "13.025.150-1", new DateOnly(2026, 1, 2));
        Insert(db, "OTRO 2 ENERO", "16.487.222-K", new DateOnly(2026, 1, 2));
        Insert(db, "CITADO 20 ENERO", "5.667.048-3", new DateOnly(2026, 1, 20));
        Insert(db, "CITADO 3 FEBRERO", "10.904.318-4", new DateOnly(2026, 2, 3));
        return db;
    }

    private static string[] Names(IReadOnlyList<FolderCase> cases) => [.. cases.Select(c => c.FullName!)];

    [Fact]
    public void Without_a_period_the_list_brings_everything_pending()
    {
        using var db = Seeded();

        var listed = db.Cases.ForSector(FolderSector.Archivo, onlyMarked: true, includePrinted: false);

        Assert.Equal(4, listed.Count);
    }

    [Fact]
    public void A_single_day_brings_only_that_days_citations()
    {
        using var db = Seeded();

        var listed = db.Cases.ForSector(FolderSector.Archivo, onlyMarked: true, includePrinted: false,
            citationDay: new DateOnly(2026, 1, 2));

        Assert.Equal(2, listed.Count);
        Assert.All(listed, item => Assert.Equal(new DateOnly(2026, 1, 2), item.CitationDate));
    }

    [Fact]
    public void A_month_brings_every_citation_of_that_month()
    {
        using var db = Seeded();

        var listed = db.Cases.ForSector(FolderSector.Archivo, onlyMarked: true, includePrinted: false,
            year: 2026, month: 1);

        Assert.Equal(3, listed.Count);
        Assert.DoesNotContain("CITADO 3 FEBRERO", Names(listed));
    }

    /// <summary>A day is more specific than a month, so asking for both obeys the day.</summary>
    [Fact]
    public void The_day_wins_over_the_month_when_both_are_given()
    {
        using var db = Seeded();

        var listed = db.Cases.ForSector(FolderSector.Archivo, onlyMarked: true, includePrinted: false,
            citationDay: new DateOnly(2026, 1, 20), year: 2026, month: 1);

        Assert.Equal("CITADO 20 ENERO", Assert.Single(listed).FullName);
    }

    [Fact]
    public void A_period_with_nothing_in_it_returns_an_empty_list()
    {
        using var db = Seeded();

        Assert.Empty(db.Cases.ForSector(FolderSector.Archivo, onlyMarked: true, includePrinted: false,
            citationDay: new DateOnly(2026, 7, 7)));
    }

    /// <summary>Narrowing by period never overrides the sector: a July-2024 folder is Oficina 43
    /// even if it was cited the same day as an Archivo one.</summary>
    [Fact]
    public void The_period_does_not_mix_sectors()
    {
        using var db = Seeded();
        db.Cases.Insert(new FolderCase
        {
            FullName = "OFICINA 43",
            Rut = "18.359.219-K",
            CitationDate = new DateOnly(2026, 1, 2),
            Office = Office.AvenidaArgentina,
            LastFolderDate = new DateOnly(2024, 7, 1),
            Marked = true
        });

        var archivo = db.Cases.ForSector(FolderSector.Archivo, onlyMarked: true, includePrinted: false,
            citationDay: new DateOnly(2026, 1, 2));

        Assert.Equal(2, archivo.Count);
        Assert.DoesNotContain("OFICINA 43", Names(archivo));
    }
}
