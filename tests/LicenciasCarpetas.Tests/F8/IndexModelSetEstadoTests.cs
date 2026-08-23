using LicenciasCarpetas.Dashboard.Pages.F8;
using LicenciasCarpetas.Domain;
using LicenciasCarpetas.F8.Domain;
using LicenciasCarpetas.F8.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure;
using Microsoft.AspNetCore.Routing;

namespace LicenciasCarpetas.Tests.F8;

/// <summary>Exercises IndexModel.OnPostSetEstado's propagation to Casos — same idea as
/// "Solicitar Cambios de Domicilio"'s Estado dropdown propagating to the source FolderCase, but
/// matched by RUT instead of a stored case id, since an UrgentRequest can also come from the
/// matriz Excel import with no FolderCase behind it at all.</summary>
public class IndexModelSetEstadoTests
{
    private sealed class NoopEmailSender : IEmailSender
    {
        public Task SendAsync(string to, string subject, string body,
            IReadOnlyList<EmailAttachment>? attachments = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private static IndexModel BuildModel(SqliteTestDatabase db, FakeUrgentRequestRepositoryForCasos urgentRequests)
    {
        var httpContext = new DefaultHttpContext();
        return new IndexModel(urgentRequests, new NoopEmailSender(), db.Cases)
        {
            PageContext = new PageContext(new ActionContext(httpContext, new RouteData(), new PageActionDescriptor()))
        };
    }

    [Fact]
    public void Setting_a_known_estado_propagates_the_matching_folder_state_to_the_case_with_the_same_rut()
    {
        using var db = new SqliteTestDatabase();
        var caseId = db.Cases.Insert(new FolderCase
        {
            FullName = "JUAN PEREZ",
            Rut = "13.025.150-1",
            Office = Office.AvenidaArgentina,
            FolderState = FolderState.PrimeraLicencia
        });
        var urgentRequests = new FakeUrgentRequestRepositoryForCasos();
        var requestId = urgentRequests.Insert(new UrgentRequest { Rut = "13025150-1", NombreCompleto = "JUAN PEREZ" });
        var model = BuildModel(db, urgentRequests);

        model.OnPostSetEstado(requestId, "SUBIR CON F8");

        Assert.Equal(FolderState.SubidaConF8, db.Cases.FindById(caseId)!.FolderState);
    }

    /// <summary>"PENDIENTE"/"CARPETA SUBIDA"/"DENEGADA" no tienen un FolderState equivalente claro
    /// — no deben tocar Casos en absoluto, ni siquiera para borrarle el estado.</summary>
    [Fact]
    public void Setting_an_estado_without_a_clear_folderstate_equivalent_leaves_the_case_untouched()
    {
        using var db = new SqliteTestDatabase();
        var caseId = db.Cases.Insert(new FolderCase
        {
            FullName = "JUAN PEREZ",
            Rut = "13.025.150-1",
            Office = Office.AvenidaArgentina,
            FolderState = FolderState.PrimeraLicencia
        });
        var urgentRequests = new FakeUrgentRequestRepositoryForCasos();
        var requestId = urgentRequests.Insert(new UrgentRequest { Rut = "13025150-1", NombreCompleto = "JUAN PEREZ" });
        var model = BuildModel(db, urgentRequests);

        model.OnPostSetEstado(requestId, "PENDIENTE");

        Assert.Equal(FolderState.PrimeraLicencia, db.Cases.FindById(caseId)!.FolderState);
    }

    [Fact]
    public void No_case_with_that_rut_does_not_throw()
    {
        using var db = new SqliteTestDatabase();
        var urgentRequests = new FakeUrgentRequestRepositoryForCasos();
        var requestId = urgentRequests.Insert(new UrgentRequest { Rut = "13025150-1", NombreCompleto = "JUAN PEREZ" });
        var model = BuildModel(db, urgentRequests);

        var result = model.OnPostSetEstado(requestId, "SUBIR CON F8");

        Assert.NotNull(result);
    }

    /// <summary>RUT repetido entre dos FolderCase (la propia pantalla de Casos ya sabe que eso
    /// pasa) — la propagación actualiza los dos, no solo el primero, para no dejar uno
    /// desincronizado silenciosamente.</summary>
    [Fact]
    public void A_shared_rut_updates_every_matching_case()
    {
        using var db = new SqliteTestDatabase();
        var firstId = db.Cases.Insert(new FolderCase
        {
            FullName = "JUAN PEREZ", Rut = "13.025.150-1", Office = Office.AvenidaArgentina
        });
        var secondId = db.Cases.Insert(new FolderCase
        {
            FullName = "JUAN PEREZ OTRO CASO", Rut = "13.025.150-1", Office = Office.AvenidaArgentina
        });
        var urgentRequests = new FakeUrgentRequestRepositoryForCasos();
        var requestId = urgentRequests.Insert(new UrgentRequest { Rut = "13025150-1", NombreCompleto = "JUAN PEREZ" });
        var model = BuildModel(db, urgentRequests);

        model.OnPostSetEstado(requestId, "SUBIR CON F8");

        Assert.Equal(FolderState.SubidaConF8, db.Cases.FindById(firstId)!.FolderState);
        Assert.Equal(FolderState.SubidaConF8, db.Cases.FindById(secondId)!.FolderState);
    }
}
