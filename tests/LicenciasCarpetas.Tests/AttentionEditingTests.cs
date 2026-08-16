using LicenciasCarpetas.Domain;
using LicenciasCarpetas.Persistence;

namespace LicenciasCarpetas.Tests;

/// <summary>
/// "ATENCIÓN" es una nota libre del libro (por ejemplo "SI, EN AV. ARGENTINA"). Al importar sirve
/// para deducir si la persona asistió, pero desde que existe la casilla "Atendido" la asistencia ya
/// no depende de ese texto: editar la nota no marca ni desmarca a nadie.
/// Ver <see cref="AttendanceAndUploadDateTests"/>.
/// </summary>
public class AttentionEditingTests
{
    private static long InsertCase(SqliteTestDatabase db, string? attention, bool attended)
        => db.Cases.Insert(new FolderCase
        {
            FullName = "JUAN PEREZ",
            Rut = "13.025.150-1",
            CitationDate = new DateOnly(2026, 1, 2),
            Office = Office.AvenidaArgentina,
            AttentionNote = attention,
            Attended = attended
        });

    [Fact]
    public void The_note_is_stored_as_written()
    {
        using var db = new SqliteTestDatabase();
        var id = InsertCase(db, attention: null, attended: false);

        db.Cases.UpdateEditableFields(id, "JUAN PEREZ", "13.025.150-1", new DateOnly(2026, 1, 2),
            null, null, null, null, null, null, "SI, EN AV. ARGENTINA", needsReview: false);

        Assert.Equal("SI, EN AV. ARGENTINA", db.Cases.FindById(id)!.AttentionNote);
    }

    [Fact]
    public void The_note_can_be_cleared()
    {
        using var db = new SqliteTestDatabase();
        var id = InsertCase(db, attention: "SI, EN AV. ARGENTINA", attended: true);

        db.Cases.UpdateEditableFields(id, "JUAN PEREZ", "13.025.150-1", new DateOnly(2026, 1, 2),
            null, null, null, null, null, null, null, needsReview: false);

        Assert.Null(db.Cases.FindById(id)!.AttentionNote);
    }

    /// <summary>
    /// Ésta es la regresión que costó datos: la asistencia se derivaba de la nota, así que guardar
    /// una fila con ese campo vacío marcaba como ausente a alguien que sí había ido, y el porcentaje
    /// de atención bajaba solo.
    /// </summary>
    [Fact]
    public void Clearing_the_note_no_longer_marks_the_person_as_absent()
    {
        using var db = new SqliteTestDatabase();
        var id = InsertCase(db, attention: "SI, EN AV. ARGENTINA", attended: true);

        db.Cases.UpdateEditableFields(id, "JUAN PEREZ", "13.025.150-1", new DateOnly(2026, 1, 2),
            null, null, null, null, null, null, null, needsReview: false);

        Assert.True(db.Cases.FindById(id)!.Attended);
        Assert.Equal(1, db.Cases.DailyAttendance(2026, 1).Single().Attended);
    }

    [Fact]
    public void Writing_a_note_does_not_mark_attendance_by_itself()
    {
        using var db = new SqliteTestDatabase();
        var id = InsertCase(db, attention: null, attended: false);

        db.Cases.UpdateEditableFields(id, "JUAN PEREZ", "13.025.150-1", new DateOnly(2026, 1, 2),
            null, null, null, null, null, null, "SI, EN AV. ARGENTINA", needsReview: false);

        Assert.False(db.Cases.FindById(id)!.Attended);
    }

    /// <summary>Al importar el libro sí se deduce: es el único dato de asistencia que trae.</summary>
    [Fact]
    public void An_import_still_derives_attendance_from_the_note()
    {
        var mapped = LicenciasCarpetas.Import.AgendaRowMapper.Map(
            new LicenciasCarpetas.Import.RawAgendaRow(
                new DateTime(2026, 1, 2), null, null, null, null, "JUAN PEREZ", "13.025.150-1",
                "SI, EN AV. ARGENTINA", null, null, null),
            new LicenciasCarpetas.Import.AgendaSheet("ENERO AV. ARGENTINA", Office.AvenidaArgentina, 1),
            rowNumber: 3);

        Assert.True(mapped!.Attended);
    }
}
