using LicenciasCarpetas.Configuration;
using LicenciasCarpetas.Dashboard.Pages;
using LicenciasCarpetas.Domain;
using LicenciasCarpetas.Persistence;
using LicenciasCarpetas.Reporting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;

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
        var model = IndexModelTestFactory.Create(db, new NullExporter(), new CarpetasOptions());

        model.OnPostSave(id, "JUAN PEREZ", "13.025.150-1", "02-01-2026", null, null, null, null, null, null);

        // Sin sesión (fuera de una petición) queda sin autor en vez de reventar.
        Assert.Null(db.Cases.FindById(id)!.UpdatedBy);
    }

    /// <summary>Autoguardado (ver autoSaveCaseForm en Index.cshtml): el fetch() manda este mismo
    /// header para pedir una respuesta JSON en vez de la redirección normal — sin recargar la
    /// página, cada cambio de celda no tendría cómo saber si el guardado funcionó o no.</summary>
    [Fact]
    public void An_ajax_request_gets_a_json_result_instead_of_a_redirect()
    {
        using var db = new SqliteTestDatabase();
        var id = Insert(db);
        var model = IndexModelTestFactory.Create(db, new NullExporter(), new CarpetasOptions());
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
        model.PageContext = new PageContext(new ActionContext(httpContext, new RouteData(), new PageActionDescriptor()));

        var result = model.OnPostSave(id, "JUAN PEREZ SOTO", "13.025.150-1", "02-01-2026", null, null, null, null, null, null);

        Assert.IsType<JsonResult>(result);
        Assert.Equal("JUAN PEREZ SOTO", db.Cases.FindById(id)!.FullName);
    }

    /// <summary>Antes esta rama en particular quedaba antes del chequeo del header X-Requested-With
    /// y siempre redirigía — un fetch() del autoguardado contra un caso borrado por otro operador
    /// mientras tanto recibía HTML de vuelta en vez de JSON, y el response.json() del cliente
    /// reventaba con un mensaje genérico de "sin conexión" que ocultaba la causa real.</summary>
    [Fact]
    public void An_ajax_request_for_a_deleted_case_still_gets_a_json_result()
    {
        using var db = new SqliteTestDatabase();
        var model = IndexModelTestFactory.Create(db, new NullExporter(), new CarpetasOptions());
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
        model.PageContext = new PageContext(new ActionContext(httpContext, new RouteData(), new PageActionDescriptor()));

        var result = model.OnPostSave(id: 999_999, "X", null, null, null, null, null, null, null, null);

        Assert.IsType<JsonResult>(result);
    }

    /// <summary>Elegir "NO EXISTE CARPETA" en Casos crea automáticamente la fila espejo en F8
    /// Urgentes — mismo dato que hoy carga el Excel de la matriz, disparado por el desplegable en
    /// vez de por Sincronizar (ver IndexModel.OnPostSave).</summary>
    [Fact]
    public void Choosing_NoExisteCarpeta_auto_creates_the_matching_F8_urgent_request()
    {
        using var db = new SqliteTestDatabase();
        var id = Insert(db);
        var model = IndexModelTestFactory.CreateWithUrgentRequests(db, new NullExporter(), new CarpetasOptions(), out var urgentRequests);

        model.OnPostSave(id, "JUAN PEREZ", "13.025.150-1", "02-01-2026", null, null,
            FolderState.NoExisteCarpeta, null, null, null);

        var created = Assert.Single(urgentRequests.GetAll());
        Assert.Equal("JUAN PEREZ", created.NombreCompleto);
        Assert.Equal("13.025.150-1", created.Rut);
        Assert.Equal("Casos", created.Origin);
        Assert.False(created.NeedsReview);
    }

    /// <summary>Sin nombre (el mismo dato que ya deja al propio caso en revisión), la fila de F8
    /// tiene que salir marcada NeedsReview — sin esto quedaba una fila incompleta mezclada entre
    /// las normales, sin el resaltado que el operador usa para encontrarlas.</summary>
    [Fact]
    public void An_urgent_request_created_with_an_incomplete_row_is_flagged_for_review()
    {
        using var db = new SqliteTestDatabase();
        var id = Insert(db);
        var model = IndexModelTestFactory.CreateWithUrgentRequests(db, new NullExporter(), new CarpetasOptions(), out var urgentRequests);

        model.OnPostSave(id, nombre: null, "13.025.150-1", "02-01-2026", null, null,
            FolderState.NoExisteCarpeta, null, null, null);

        Assert.True(Assert.Single(urgentRequests.GetAll()).NeedsReview);
    }

    /// <summary>Documenta el comportamiento actual (compartido con MatrizSyncService): el dedup es
    /// por RUT en todo F8, no por caso — un segundo FolderCase con el mismo RUT (la app ya permite
    /// RUTs repetidos, ver DuplicateRuts) no genera una segunda fila en F8 aunque sea un caso
    /// distinto. Si esto cambia algún día, este test debe fallar y avisar.</summary>
    [Fact]
    public void A_second_case_sharing_the_same_rut_does_not_get_its_own_urgent_request()
    {
        using var db = new SqliteTestDatabase();
        var firstId = Insert(db);
        var secondId = db.Cases.Insert(new FolderCase
        {
            FullName = "JUAN PEREZ OTRO CASO",
            Rut = "13.025.150-1",
            CitationDate = new DateOnly(2026, 2, 1),
            Office = Office.AvenidaArgentina
        });
        var model = IndexModelTestFactory.CreateWithUrgentRequests(db, new NullExporter(), new CarpetasOptions(), out var urgentRequests);

        model.OnPostSave(firstId, "JUAN PEREZ", "13.025.150-1", "02-01-2026", null, null,
            FolderState.NoExisteCarpeta, null, null, null);
        model.OnPostSave(secondId, "JUAN PEREZ OTRO CASO", "13.025.150-1", "01-02-2026", null, null,
            FolderState.NoExisteCarpeta, null, null, null);

        Assert.Single(urgentRequests.GetAll());
    }

    /// <summary>El autoguardado reenvía el form entero en cada cambio de celda — sin este chequeo,
    /// tipear cualquier otro dato de la misma fila (con el estado ya en NO EXISTE CARPETA) crearía
    /// una fila nueva en F8 cada vez.</summary>
    [Fact]
    public void Saving_the_same_row_again_does_not_duplicate_the_urgent_request()
    {
        using var db = new SqliteTestDatabase();
        var id = Insert(db);
        var model = IndexModelTestFactory.CreateWithUrgentRequests(db, new NullExporter(), new CarpetasOptions(), out var urgentRequests);

        model.OnPostSave(id, "JUAN PEREZ", "13.025.150-1", "02-01-2026", null, null,
            FolderState.NoExisteCarpeta, null, null, null);
        model.OnPostSave(id, "JUAN PEREZ", "13.025.150-1", "02-01-2026", null, null,
            FolderState.NoExisteCarpeta, null, null, null, codigoF8: "F8-123");

        Assert.Single(urgentRequests.GetAll());
    }

    [Fact]
    public void Choosing_a_different_state_does_not_create_an_urgent_request()
    {
        using var db = new SqliteTestDatabase();
        var id = Insert(db);
        var model = IndexModelTestFactory.CreateWithUrgentRequests(db, new NullExporter(), new CarpetasOptions(), out var urgentRequests);

        model.OnPostSave(id, "JUAN PEREZ", "13.025.150-1", "02-01-2026", null, null,
            FolderState.SubidaAConaset, null, null, null);

        Assert.Empty(urgentRequests.GetAll());
    }
}
