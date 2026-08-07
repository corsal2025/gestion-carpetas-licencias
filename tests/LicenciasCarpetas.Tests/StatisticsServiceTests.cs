using LicenciasCarpetas.Domain;
using LicenciasCarpetas.Statistics;

namespace LicenciasCarpetas.Tests;

public class StatisticsServiceTests
{
    private static FolderCase Case(int day, Office office, bool attended, string rut, FolderState state = FolderState.SubidaAConaset)
        => new()
        {
            FullName = $"PERSONA {rut}",
            Rut = rut,
            CitationDate = new DateOnly(2026, 1, day),
            Office = office,
            Attended = attended,
            FolderState = state,
            FinalDecision = FinalDecision.Otorgado
        };

    [Fact]
    public void Rebuilds_the_daily_sheet_from_cases_and_manual_counters()
    {
        using var db = new SqliteTestDatabase();
        db.Cases.Insert(Case(2, Office.AvenidaArgentina, attended: true, "13.025.150-1"));
        db.Cases.Insert(Case(2, Office.AvenidaArgentina, attended: false, "16.487.222-K"));
        db.Cases.Insert(Case(2, Office.Placilla, attended: true, "5.667.048-3"));
        db.Counters.Upsert(new DailyCounter { Date = new DateOnly(2026, 1, 2), Scanned = 44, Uploaded = 37 });

        var statistics = new StatisticsService(db.Cases, db.Counters).ForMonth(2026, 1);

        var day = Assert.Single(statistics.Days);
        Assert.Equal(new DateOnly(2026, 1, 2), day.Date);
        Assert.Equal(44, day.Scanned);
        Assert.Equal(37, day.Uploaded);
        Assert.Equal(3, day.ScheduledTotal);
        Assert.Equal(2, day.AttendedTotal);
        Assert.Equal(0.5, day.AttendanceRate(Office.AvenidaArgentina));
        Assert.Equal(1.0, day.AttendanceRate(Office.Placilla));
        Assert.Null(day.AttendanceRate(Office.MercadoPuerto));
    }

    [Fact]
    public void Totals_the_month_and_breaks_it_down_by_catalog_value()
    {
        using var db = new SqliteTestDatabase();
        db.Cases.Insert(Case(2, Office.AvenidaArgentina, attended: true, "13.025.150-1"));
        db.Cases.Insert(Case(5, Office.AvenidaArgentina, attended: true, "16.487.222-K", FolderState.PrimeraLicencia));
        db.Cases.Insert(Case(5, Office.AvenidaArgentina, attended: false, "5.667.048-3", FolderState.PrimeraLicencia));

        var statistics = new StatisticsService(db.Cases, db.Counters).ForMonth(2026, 1);

        Assert.Equal(2, statistics.Days.Count);
        Assert.Equal(3, statistics.ScheduledTotal);
        Assert.Equal(2, statistics.AttendedTotal);
        Assert.Equal(2d / 3d, statistics.AttendanceRate);

        var primeraLicencia = statistics.FolderStates.Single(entry => entry.State == FolderState.PrimeraLicencia);
        Assert.Equal(2, primeraLicencia.Count);
        Assert.Equal(3, statistics.FinalDecisions.Single(entry => entry.Decision == FinalDecision.Otorgado).Count);
    }

    [Fact]
    public void A_day_with_counters_but_no_citations_still_shows_up()
    {
        using var db = new SqliteTestDatabase();
        db.Counters.Upsert(new DailyCounter { Date = new DateOnly(2026, 1, 3), Scanned = 12, Uploaded = null });

        var statistics = new StatisticsService(db.Cases, db.Counters).ForMonth(2026, 1);

        var day = Assert.Single(statistics.Days);
        Assert.Equal(12, day.Scanned);
        Assert.Equal(0, day.ScheduledTotal);
        Assert.Null(day.AttendanceRate(Office.AvenidaArgentina));
    }

    [Fact]
    public void A_null_counter_does_not_erase_the_value_already_stored()
    {
        using var db = new SqliteTestDatabase();
        db.Counters.Upsert(new DailyCounter { Date = new DateOnly(2026, 1, 3), Scanned = 12, Uploaded = 9 });
        db.Counters.Upsert(new DailyCounter { Date = new DateOnly(2026, 1, 3), Scanned = null, Uploaded = 11 });

        var counter = db.Counters.Find(new DateOnly(2026, 1, 3))!;
        Assert.Equal(12, counter.Scanned);
        Assert.Equal(11, counter.Uploaded);
    }
}
