using LicenciasCarpetas.Configuration;
using LicenciasCarpetas.Dashboard.Pages;
using LicenciasCarpetas.Domain;
using LicenciasCarpetas.Persistence;
using LicenciasCarpetas.Reporting;

namespace LicenciasCarpetas.Tests;

/// <summary>
/// Once a folder is uploaded to Conaset the case is finished work: it drops to the end of the list,
/// ordered by when it went up, and the upload date writes itself so nobody has to remember it.
/// </summary>
public class ConasetUploadTests
{
    private sealed class NullExporter : IExcelCaseExporter
    {
        public byte[] Export(IReadOnlyList<FolderCase> cases, string sheetTitle) => [];
    }

    private static long Insert(SqliteTestDatabase db, string name, string rut, int citationDay,
        FolderState? state = null, DateOnly? uploaded = null)
        => db.Cases.Insert(new FolderCase
        {
            FullName = name,
            Rut = rut,
            CitationDate = new DateOnly(2026, 1, citationDay),
            Office = Office.AvenidaArgentina,
            FolderState = state,
            FolderUploadedDate = uploaded
        });

    private static string[] Names(IReadOnlyList<FolderCase> cases) => [.. cases.Select(c => c.FullName!)];

    [Fact]
    public void Uploaded_cases_sink_to_the_end_of_the_list()
    {
        using var db = new SqliteTestDatabase();
        Insert(db, "SUBIDA TEMPRANO", "13.025.150-1", 20, FolderState.SubidaAConaset, new DateOnly(2026, 2, 1));
        Insert(db, "PENDIENTE VIEJO", "16.487.222-K", 2);
        Insert(db, "PENDIENTE NUEVO", "5.667.048-3", 25);

        var listed = db.Cases.Query(new CaseFilter(), 0, 50);

        Assert.Equal(["PENDIENTE NUEVO", "PENDIENTE VIEJO", "SUBIDA TEMPRANO"], Names(listed));
    }

    [Fact]
    public void Uploaded_cases_are_ordered_by_when_they_were_uploaded()
    {
        using var db = new SqliteTestDatabase();
        Insert(db, "TERCERA", "13.025.150-1", 2, FolderState.SubidaAConaset, new DateOnly(2026, 3, 10));
        Insert(db, "PRIMERA", "16.487.222-K", 2, FolderState.SubidaAConaset, new DateOnly(2026, 1, 5));
        Insert(db, "SEGUNDA", "5.667.048-3", 2, FolderState.SubidaAConaset, new DateOnly(2026, 2, 8));

        var listed = db.Cases.Query(new CaseFilter(), 0, 50);

        Assert.Equal(["PRIMERA", "SEGUNDA", "TERCERA"], Names(listed));
    }

    /// <summary>Only the default view sinks them; asking for a column explicitly must obey.</summary>
    [Fact]
    public void An_explicit_sort_still_wins()
    {
        using var db = new SqliteTestDatabase();
        Insert(db, "ANA UPLOADED", "13.025.150-1", 2, FolderState.SubidaAConaset, new DateOnly(2026, 2, 1));
        Insert(db, "ZOE PENDING", "16.487.222-K", 2);

        var listed = db.Cases.Query(new CaseFilter { Sort = CaseSort.Name }, 0, 50);

        Assert.Equal(["ANA UPLOADED", "ZOE PENDING"], Names(listed));
    }

    [Fact]
    public void Setting_the_state_to_conaset_stamps_today_as_the_upload_date()
    {
        using var db = new SqliteTestDatabase();
        var id = Insert(db, "JUAN PEREZ", "13.025.150-1", 2);
        var model = IndexModelTestFactory.Create(db, new NullExporter(), new CarpetasOptions());

        model.OnPostSave(id, "JUAN PEREZ", "13.025.150-1", "02-01-2026", subida: null,
            ultimaCarpeta: null, estado: FolderState.SubidaAConaset, decision: null, idoneidad: null, atencion: null);

        Assert.Equal(DateOnly.FromDateTime(DateTime.Today), db.Cases.FindById(id)!.FolderUploadedDate);
    }

    [Fact]
    public void An_upload_date_already_written_is_respected()
    {
        using var db = new SqliteTestDatabase();
        var id = Insert(db, "JUAN PEREZ", "13.025.150-1", 2);
        var model = IndexModelTestFactory.Create(db, new NullExporter(), new CarpetasOptions());

        model.OnPostSave(id, "JUAN PEREZ", "13.025.150-1", "02-01-2026", subida: "05-01-2026",
            ultimaCarpeta: null, estado: FolderState.SubidaAConaset, decision: null, idoneidad: null, atencion: null);

        Assert.Equal(new DateOnly(2026, 1, 5), db.Cases.FindById(id)!.FolderUploadedDate);
    }

    [Fact]
    public void Any_other_state_leaves_the_upload_date_empty()
    {
        using var db = new SqliteTestDatabase();
        var id = Insert(db, "JUAN PEREZ", "13.025.150-1", 2);
        var model = IndexModelTestFactory.Create(db, new NullExporter(), new CarpetasOptions());

        model.OnPostSave(id, "JUAN PEREZ", "13.025.150-1", "02-01-2026", subida: null,
            ultimaCarpeta: null, estado: FolderState.PrimeraLicencia, decision: null, idoneidad: null, atencion: null);

        Assert.Null(db.Cases.FindById(id)!.FolderUploadedDate);
    }

    /// <summary>Conaset uploads are painted blue now, not the workbook's yellow.</summary>
    [Fact]
    public void The_conaset_state_is_blue()
        => Assert.Equal("#1155CC", FolderStateCatalog.Color(FolderState.SubidaAConaset));
}
