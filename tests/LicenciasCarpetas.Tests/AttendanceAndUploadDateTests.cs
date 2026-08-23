using LicenciasCarpetas.Configuration;
using LicenciasCarpetas.Dashboard.Pages;
using LicenciasCarpetas.Domain;
using LicenciasCarpetas.Persistence;
using LicenciasCarpetas.Reporting;

namespace LicenciasCarpetas.Tests;

/// <summary>
/// Attendance is ticked every day and feeds the attendance rate on the statistics screen, so it
/// needs its own control instead of depending on free text; and every upload state stamps its date.
/// </summary>
public class AttendanceAndUploadDateTests
{
    private sealed class NullExporter : IExcelCaseExporter
    {
        public byte[] Export(IReadOnlyList<FolderCase> cases, string sheetTitle) => [];
    }

    private static long Insert(SqliteTestDatabase db, bool attended = false, string? attention = null)
        => db.Cases.Insert(new FolderCase
        {
            FullName = "JUAN PEREZ",
            Rut = "13.025.150-1",
            CitationDate = new DateOnly(2026, 1, 2),
            Office = Office.AvenidaArgentina,
            Attended = attended,
            AttentionNote = attention
        });

    [Fact]
    public void Attendance_can_be_ticked_on_its_own()
    {
        using var db = new SqliteTestDatabase();
        var id = Insert(db);

        db.Cases.SetAttended(id, true);

        Assert.True(db.Cases.FindById(id)!.Attended);
        Assert.Equal(1, db.Cases.DailyAttendance(2026, 1).Single().Attended);
    }

    [Fact]
    public void Attendance_can_be_unticked()
    {
        using var db = new SqliteTestDatabase();
        var id = Insert(db, attended: true, attention: "SI, EN AV. ARGENTINA");

        db.Cases.SetAttended(id, false);

        Assert.False(db.Cases.FindById(id)!.Attended);
    }

    /// <summary>
    /// Saving the rest of the row must not undo the tick. It used to be derived from the ATENCIÓN
    /// note, so a save with an empty note silently marked the person as absent.
    /// </summary>
    [Fact]
    public void Saving_the_row_leaves_the_attendance_tick_alone()
    {
        using var db = new SqliteTestDatabase();
        var id = Insert(db);
        db.Cases.SetAttended(id, true);
        var model = IndexModelTestFactory.Create(db, new NullExporter(), new CarpetasOptions());

        model.OnPostSave(id, "JUAN PEREZ", "13.025.150-1", "02-01-2026", subida: null, ultimaCarpeta: null,
            estado: null, decision: null, idoneidad: null, atencion: null);

        Assert.True(db.Cases.FindById(id)!.Attended);
    }

    /// <summary>Every upload state is an upload: all of them stamp the day it happened.</summary>
    [Theory]
    [InlineData(FolderState.SubidaAConaset)]
    [InlineData(FolderState.SubidaConF8)]
    [InlineData(FolderState.SubidaConOficio)]
    [InlineData(FolderState.CambioDomicilioSubidoAConaset)]
    [InlineData(FolderState.CambioDomicilioSubidoConCorreo)]
    public void Any_upload_state_stamps_todays_date(FolderState state)
    {
        using var db = new SqliteTestDatabase();
        var id = Insert(db);
        var model = IndexModelTestFactory.Create(db, new NullExporter(), new CarpetasOptions());

        model.OnPostSave(id, "JUAN PEREZ", "13.025.150-1", "02-01-2026", subida: null, ultimaCarpeta: null,
            estado: state, decision: null, idoneidad: null, atencion: null);

        Assert.Equal(DateOnly.FromDateTime(DateTime.Today), db.Cases.FindById(id)!.FolderUploadedDate);
    }

    [Theory]
    [InlineData(FolderState.PrimeraLicencia)]
    [InlineData(FolderState.CambioDomicilioSolicitado)]
    [InlineData(FolderState.NoExisteCarpeta)]
    public void A_state_that_is_not_an_upload_stamps_nothing(FolderState state)
    {
        using var db = new SqliteTestDatabase();
        var id = Insert(db);
        var model = IndexModelTestFactory.Create(db, new NullExporter(), new CarpetasOptions());

        model.OnPostSave(id, "JUAN PEREZ", "13.025.150-1", "02-01-2026", subida: null, ultimaCarpeta: null,
            estado: state, decision: null, idoneidad: null, atencion: null);

        Assert.Null(db.Cases.FindById(id)!.FolderUploadedDate);
    }

    [Fact]
    public void A_new_case_can_be_created_with_the_f8_code_and_the_previous_folder_date()
    {
        using var db = new SqliteTestDatabase();
        var model = IndexModelTestFactory.Create(db, new NullExporter(), new CarpetasOptions());

        model.OnPostAdd("MARIA SOTO", "16.487.222-K", "02-01-2026", Office.Placilla,
            ultimaCarpeta: "01-03-2024", estado: null, decision: null, idoneidad: null, atencion: null,
            penultima: "15-04-2015", codigoF8: "F8-2026-200");

        var created = Assert.Single(db.Cases.Query(new CaseFilter(), 0, 10));
        Assert.Equal(new DateOnly(2015, 4, 15), created.PenultimateFolderDate);
        Assert.Equal("F8-2026-200", created.CodigoF8);
    }
}
