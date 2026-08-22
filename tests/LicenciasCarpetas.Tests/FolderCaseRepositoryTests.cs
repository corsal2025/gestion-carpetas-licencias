using LicenciasCarpetas.Domain;
using LicenciasCarpetas.Persistence;

namespace LicenciasCarpetas.Tests;

public class FolderCaseRepositoryTests
{
    private static FolderCase Case(
        string fullName = "JUAN PEREZ",
        string? rut = "13.025.150-1",
        int day = 2,
        Office office = Office.AvenidaArgentina,
        string sheet = "ENERO AV. ARGENTINA",
        int row = 3,
        FolderState? state = FolderState.SubidaAConaset,
        DateOnly? lastFolder = null,
        bool attended = true)
        => new()
        {
            FullName = fullName,
            Rut = rut,
            CitationDate = new DateOnly(2026, 1, day),
            Office = office,
            FolderState = state,
            LastFolderDate = lastFolder,
            Attended = attended,
            SourceSheet = sheet,
            SourceRow = row
        };

    [Fact]
    public void Inserts_and_reads_back_a_case()
    {
        using var db = new SqliteTestDatabase();

        var id = db.Cases.Insert(Case());
        var stored = db.Cases.FindById(id);

        Assert.NotNull(stored);
        Assert.Equal("JUAN PEREZ", stored.FullName);
        Assert.Equal(new DateOnly(2026, 1, 2), stored.CitationDate);
        Assert.Equal(FolderState.SubidaAConaset, stored.FolderState);
    }

    [Fact]
    public void Re_importing_the_same_person_updates_instead_of_duplicating()
    {
        using var db = new SqliteTestDatabase();

        Assert.Equal(UpsertOutcome.Inserted, db.Cases.Upsert(Case()));
        // Same person, same citation, same office — but the operator moved the row and changed the state.
        Assert.Equal(UpsertOutcome.Updated, db.Cases.Upsert(Case(row: 57, state: FolderState.SubidaConF8)));

        var all = db.Cases.Query(new CaseFilter(), 0, 50);
        Assert.Single(all);
        Assert.Equal(FolderState.SubidaConF8, all[0].FolderState);
    }

    [Fact]
    public void A_row_without_a_valid_rut_is_matched_by_its_workbook_cell()
    {
        using var db = new SqliteTestDatabase();

        Assert.Equal(UpsertOutcome.Inserted, db.Cases.Upsert(Case(rut: null, row: 12)));
        Assert.Equal(UpsertOutcome.Updated, db.Cases.Upsert(Case(rut: null, row: 12, fullName: "JUAN PEREZ SOTO")));

        var all = db.Cases.Query(new CaseFilter(), 0, 50);
        Assert.Single(all);
        Assert.Equal("JUAN PEREZ SOTO", all[0].FullName);
    }

    [Fact]
    public void Different_people_on_the_same_day_are_separate_cases()
    {
        using var db = new SqliteTestDatabase();

        db.Cases.Upsert(Case(rut: "13.025.150-1", row: 3));
        db.Cases.Upsert(Case(rut: "16.487.222-K", row: 4, fullName: "MARGARITA PARRAGUEZ"));

        Assert.Equal(2, db.Cases.Count(new CaseFilter()));
    }

    [Fact]
    public void Filters_by_office_month_and_state()
    {
        using var db = new SqliteTestDatabase();

        db.Cases.Upsert(Case(rut: "13.025.150-1", row: 3));
        db.Cases.Upsert(Case(rut: "16.487.222-K", row: 4, office: Office.Placilla, sheet: "ENERO PLACILLA"));
        db.Cases.Upsert(Case(rut: "5.667.048-3", row: 5, state: FolderState.PrimeraLicencia));

        Assert.Equal(2, db.Cases.Count(new CaseFilter { Office = Office.AvenidaArgentina }));
        Assert.Equal(3, db.Cases.Count(new CaseFilter { Year = 2026, Month = 1 }));
        Assert.Equal(0, db.Cases.Count(new CaseFilter { Year = 2026, Month = 2 }));
        Assert.Equal(1, db.Cases.Count(new CaseFilter { FolderState = FolderState.PrimeraLicencia }));
    }

    /// <summary>CitationDay powers Estadísticas' "marcar asistencia" bulk action — needs one exact
    /// day's agenda, independent of Year/Month (which only narrow to a whole month).</summary>
    [Fact]
    public void Filters_by_exact_citation_day()
    {
        using var db = new SqliteTestDatabase();

        db.Cases.Upsert(Case(rut: "13.025.150-1", row: 3, day: 2));
        db.Cases.Upsert(Case(rut: "16.487.222-K", row: 4, day: 2));
        db.Cases.Upsert(Case(rut: "5.667.048-3", row: 5, day: 5));

        var day2 = db.Cases.QueryAll(new CaseFilter { CitationDay = new DateOnly(2026, 1, 2) });
        var day5 = db.Cases.QueryAll(new CaseFilter { CitationDay = new DateOnly(2026, 1, 5) });
        var day9 = db.Cases.QueryAll(new CaseFilter { CitationDay = new DateOnly(2026, 1, 9) });

        Assert.Equal(2, day2.Count);
        Assert.Single(day5);
        Assert.Empty(day9);
    }

    [Fact]
    public void Searches_a_rut_typed_without_dots()
    {
        using var db = new SqliteTestDatabase();
        db.Cases.Upsert(Case(rut: "13.025.150-1"));

        Assert.Equal(1, db.Cases.Count(new CaseFilter { Search = "130251501" }));
        Assert.Equal(1, db.Cases.Count(new CaseFilter { Search = "perez" }));
        Assert.Equal(0, db.Cases.Count(new CaseFilter { Search = "gonzalez" }));
    }

    [Fact]
    public void Splits_cases_between_archivo_and_oficina_43()
    {
        using var db = new SqliteTestDatabase();

        db.Cases.Upsert(Case(rut: "13.025.150-1", row: 3, lastFolder: new DateOnly(2023, 6, 30)));
        db.Cases.Upsert(Case(rut: "16.487.222-K", row: 4, lastFolder: new DateOnly(2023, 7, 1)));

        Assert.Single(db.Cases.ForSector(FolderSector.Archivo, onlyMarked: false));
        Assert.Single(db.Cases.ForSector(FolderSector.Oficina43, onlyMarked: false));
    }

    [Fact]
    public void Only_marked_cases_reach_the_printable_sector_list_when_asked()
    {
        using var db = new SqliteTestDatabase();

        var id = db.Cases.Insert(Case(lastFolder: new DateOnly(2020, 1, 1)));
        db.Cases.Insert(Case(rut: "16.487.222-K", row: 4, lastFolder: new DateOnly(2020, 1, 1)));
        db.Cases.SetMarked(id, true);

        Assert.Single(db.Cases.ForSector(FolderSector.Archivo, onlyMarked: true));
        Assert.Equal(2, db.Cases.ForSector(FolderSector.Archivo, onlyMarked: false).Count);
    }

    [Fact]
    public void Counts_attendance_per_day_and_office()
    {
        using var db = new SqliteTestDatabase();

        db.Cases.Upsert(Case(rut: "13.025.150-1", row: 3, attended: true));
        db.Cases.Upsert(Case(rut: "16.487.222-K", row: 4, attended: false));
        db.Cases.Upsert(Case(rut: "5.667.048-3", row: 5, office: Office.Placilla, sheet: "ENERO PLACILLA", attended: true));

        var attendance = db.Cases.DailyAttendance(2026, 1);

        var avArgentina = attendance.Single(entry => entry.Office == Office.AvenidaArgentina);
        Assert.Equal(2, avArgentina.Scheduled);
        Assert.Equal(1, avArgentina.Attended);

        var placilla = attendance.Single(entry => entry.Office == Office.Placilla);
        Assert.Equal(1, placilla.Scheduled);
        Assert.Equal(1, placilla.Attended);
    }

    [Fact]
    public void Editing_a_case_clears_the_raw_leftovers_and_the_review_flag()
    {
        using var db = new SqliteTestDatabase();
        var id = db.Cases.Insert(new FolderCase
        {
            FullName = "JUAN PEREZ",
            Rut = "13.025.150-9",
            Office = Office.AvenidaArgentina,
            FolderStateRaw = "ALGO RARO",
            NeedsReview = true
        });

        db.Cases.UpdateEditableFields(id, "JUAN PEREZ SOTO", "13.025.150-1", new DateOnly(2026, 1, 2),
            null, null, null, FolderState.SubidaAConaset, FinalDecision.Otorgado, MoralIdoneity.Alertada,
            "SI, EN AV. ARGENTINA", needsReview: false);

        var stored = db.Cases.FindById(id)!;
        Assert.Equal("JUAN PEREZ SOTO", stored.FullName);
        Assert.Equal("13.025.150-1", stored.Rut);
        Assert.Null(stored.FolderStateRaw);
        Assert.Equal(FolderState.SubidaAConaset, stored.FolderState);
        Assert.Equal("SI, EN AV. ARGENTINA", stored.AttentionNote);
        // La asistencia ya no se deduce del texto de ATENCIÓN: tiene su propia casilla.
        Assert.False(stored.Attended);
        Assert.False(stored.NeedsReview);
    }

    [Fact]
    public void Pages_results_without_losing_or_repeating_rows()
    {
        using var db = new SqliteTestDatabase();
        for (var i = 0; i < 25; i++)
        {
            db.Cases.Insert(Case(fullName: $"PERSONA {i:D2}", rut: null, row: i));
        }

        var firstPage = db.Cases.Query(new CaseFilter(), 0, 10);
        var secondPage = db.Cases.Query(new CaseFilter(), 10, 10);

        Assert.Equal(10, firstPage.Count);
        Assert.Equal(10, secondPage.Count);
        Assert.Empty(firstPage.Select(c => c.Id).Intersect(secondPage.Select(c => c.Id)));
        Assert.Equal(25, db.Cases.Count(new CaseFilter()));
    }

    [Fact]
    public void DeleteAllPermanently_WipesEveryRow_AndReturnsHowMany()
    {
        using var db = new SqliteTestDatabase();
        db.Cases.Upsert(Case(rut: "13.025.150-1", row: 3));
        db.Cases.Upsert(Case(rut: "16.487.222-K", row: 4));
        db.Cases.Upsert(Case(rut: "5.667.048-3", row: 5));

        var deleted = db.Cases.DeleteAllPermanently();

        Assert.Equal(3, deleted);
        Assert.Equal(0, db.Cases.Count(new CaseFilter()));
        Assert.Empty(db.Cases.Query(new CaseFilter(), 0, 100));
    }
}
