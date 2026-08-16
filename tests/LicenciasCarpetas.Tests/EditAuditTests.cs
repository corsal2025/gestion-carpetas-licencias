using LicenciasCarpetas.Configuration;
using LicenciasCarpetas.Dashboard.Pages;
using LicenciasCarpetas.Domain;
using LicenciasCarpetas.Persistence;
using LicenciasCarpetas.Reporting;

namespace LicenciasCarpetas.Tests;

/// <summary>
/// Con varias cuentas trabajando sobre la misma agenda, "esto lo cambió alguien" no sirve: cada
/// caso guarda quién lo tocó por última vez, junto a la fecha que ya guardaba.
/// </summary>
public class EditAuditTests
{
    private sealed class NullExporter : IExcelCaseExporter
    {
        public byte[] Export(IReadOnlyList<FolderCase> cases, string sheetTitle) => [];
    }

    private static long Insert(SqliteTestDatabase db) => db.Cases.Insert(new FolderCase
    {
        FullName = "JUAN PEREZ",
        Rut = "13.025.150-1",
        CitationDate = new DateOnly(2026, 1, 2),
        Office = Office.AvenidaArgentina
    });

    [Fact]
    public void An_edit_records_who_made_it()
    {
        using var db = new SqliteTestDatabase();
        var id = Insert(db);

        db.Cases.UpdateEditableFields(id, "JUAN PEREZ SOTO", "13.025.150-1", new DateOnly(2026, 1, 2),
            null, null, null, null, null, null, null, needsReview: false, editedBy: "raul");

        Assert.Equal("raul", db.Cases.FindById(id)!.UpdatedBy);
    }

    [Fact]
    public void A_case_nobody_has_edited_yet_has_no_author()
    {
        using var db = new SqliteTestDatabase();
        var id = Insert(db);

        Assert.Null(db.Cases.FindById(id)!.UpdatedBy);
    }

    [Fact]
    public void The_last_editor_replaces_the_previous_one()
    {
        using var db = new SqliteTestDatabase();
        var id = Insert(db);

        db.Cases.UpdateEditableFields(id, "A", null, null, null, null, null, null, null, null, null,
            needsReview: true, editedBy: "raul");
        db.Cases.UpdateEditableFields(id, "B", null, null, null, null, null, null, null, null, null,
            needsReview: true, editedBy: "operador");

        Assert.Equal("operador", db.Cases.FindById(id)!.UpdatedBy);
    }

    /// <summary>Una reimportación del libro no es obra de nadie: no debe atribuirse a quien editó.</summary>
    [Fact]
    public void An_import_does_not_claim_authorship()
    {
        using var db = new SqliteTestDatabase();
        var id = Insert(db);
        db.Cases.UpdateEditableFields(id, "JUAN PEREZ", null, new DateOnly(2026, 1, 2), null, null, null,
            null, null, null, null, needsReview: false, editedBy: "raul");

        db.Cases.Upsert(new FolderCase
        {
            FullName = "JUAN PEREZ",
            Rut = "13.025.150-1",
            CitationDate = new DateOnly(2026, 1, 2),
            Office = Office.AvenidaArgentina
        });

        Assert.Equal("raul", db.Cases.FindById(id)!.UpdatedBy);
    }

    [Fact]
    public void The_cases_screen_attributes_the_edit_to_the_signed_in_user()
    {
        using var db = new SqliteTestDatabase();
        var id = Insert(db);
        var model = new IndexModel(db.Cases, new NullExporter(), new CarpetasOptions());

        model.OnPostSave(id, "JUAN PEREZ", "13.025.150-1", "02-01-2026", null, null, null, null, null, null);

        // Sin sesión (fuera de una petición) queda sin autor en vez de reventar.
        Assert.Null(db.Cases.FindById(id)!.UpdatedBy);
    }
}
