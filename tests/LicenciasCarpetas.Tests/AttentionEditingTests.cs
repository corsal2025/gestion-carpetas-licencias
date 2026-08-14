using LicenciasCarpetas.Domain;
using LicenciasCarpetas.Persistence;

namespace LicenciasCarpetas.Tests;

/// <summary>
/// "ATENCIÓN" is written by hand every day in the workbook, and it is what tells whether the person
/// actually showed up — so editing it has to keep the derived Attended flag in step.
/// </summary>
public class AttentionEditingTests
{
    private static long InsertCase(SqliteTestDatabase db, string? attention)
        => db.Cases.Insert(new FolderCase
        {
            FullName = "JUAN PEREZ",
            Rut = "13.025.150-1",
            CitationDate = new DateOnly(2026, 1, 2),
            Office = Office.AvenidaArgentina,
            AttentionNote = attention,
            Attended = attention is not null
        });

    [Fact]
    public void Writing_the_attention_note_marks_the_person_as_attended()
    {
        using var db = new SqliteTestDatabase();
        var id = InsertCase(db, attention: null);

        db.Cases.UpdateEditableFields(id, "JUAN PEREZ", "13.025.150-1", new DateOnly(2026, 1, 2),
            null, null, null, null, null, null, "SI, EN AV. ARGENTINA", needsReview: false);

        var stored = db.Cases.FindById(id)!;
        Assert.Equal("SI, EN AV. ARGENTINA", stored.AttentionNote);
        Assert.True(stored.Attended);
    }

    [Fact]
    public void Clearing_the_attention_note_marks_the_person_as_not_attended()
    {
        using var db = new SqliteTestDatabase();
        var id = InsertCase(db, attention: "SI, EN AV. ARGENTINA");

        db.Cases.UpdateEditableFields(id, "JUAN PEREZ", "13.025.150-1", new DateOnly(2026, 1, 2),
            null, null, null, null, null, null, null, needsReview: false);

        var stored = db.Cases.FindById(id)!;
        Assert.Null(stored.AttentionNote);
        Assert.False(stored.Attended);
    }

    [Fact]
    public void Attendance_counts_follow_the_edited_note()
    {
        using var db = new SqliteTestDatabase();
        var id = InsertCase(db, attention: null);

        Assert.Equal(0, db.Cases.DailyAttendance(2026, 1).Single().Attended);

        db.Cases.UpdateEditableFields(id, "JUAN PEREZ", "13.025.150-1", new DateOnly(2026, 1, 2),
            null, null, null, null, null, null, "SI, EN AV. ARGENTINA", needsReview: false);

        Assert.Equal(1, db.Cases.DailyAttendance(2026, 1).Single().Attended);
    }
}
