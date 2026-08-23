using System.Security.Claims;
using LicenciasCarpetas.CambioDomicilio.Domain;
using LicenciasCarpetas.CambioDomicilio.Solicitar;
using LicenciasCarpetas.Dashboard.Pages.CambioDomicilio.Solicitar;
using LicenciasCarpetas.Domain;
using LicenciasCarpetas.F8.Services;
using LicenciasCarpetas.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;

namespace LicenciasCarpetas.Tests.CambioDomicilio.Solicitar;

/// <summary>Exercises the "Solicitar" button on the outbound-requests listing (IndexModel), which
/// sends a Borrador request directly from the table without going through Nueva.cshtml — same
/// send path (OutboundRequestSender) as NuevaModel.OnPostEnviar, just reached from a shorter click.</summary>
public class IndexModelTests
{
    private const long UserId = 42;

    private sealed class InMemoryTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object?> LoadTempData(HttpContext context) => new Dictionary<string, object?>();
        public void SaveTempData(HttpContext context, IDictionary<string, object?> values) { }
    }

    private sealed class FakeComunaContactRepository : IComunaContactRepository
    {
        private readonly List<ComunaContact> _contacts = [];
        private long _nextId = 1;

        public void EnsureSchema() { }

        public void Upsert(ComunaContact contact)
        {
            contact.Id = _nextId++;
            _contacts.Add(contact);
        }

        public IReadOnlyList<ComunaContact> All(string? search = null) => _contacts;

        public void Delete(long id) => _contacts.RemoveAll(c => c.Id == id);
    }

    private sealed class RecordingEmailSender : IEmailSender
    {
        public string? To { get; private set; }
        public int CallCount { get; private set; }

        public Task SendAsync(string to, string subject, string body,
            IReadOnlyList<EmailAttachment>? attachments = null, CancellationToken cancellationToken = default)
        {
            CallCount++;
            To = to;
            return Task.CompletedTask;
        }
    }

    private sealed record Fixture(
        IndexModel Model,
        FakeOutboundAddressChangeRequestRepository Repository,
        FakeComunaContactRepository ComunaContacts,
        RecordingEmailSender EmailSender);

    private static Fixture BuildModel()
    {
        var repository = new FakeOutboundAddressChangeRequestRepository();
        var comunaContacts = new FakeComunaContactRepository();
        var emailSender = new RecordingEmailSender();
        var sender = new OutboundRequestSender(repository, comunaContacts, emailSender);

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, UserId.ToString())], "Test"))
        };

        var model = new IndexModel(repository, sender)
        {
            PageContext = new PageContext(new ActionContext(httpContext, new RouteData(), new PageActionDescriptor())),
            TempData = new TempDataDictionary(httpContext, new InMemoryTempDataProvider())
        };

        return new Fixture(model, repository, comunaContacts, emailSender);
    }

    private static long SeedDraft(FakeOutboundAddressChangeRequestRepository repository, string? comuna = "Quillota") =>
        repository.Insert(new OutboundAddressChangeRequest
        {
            FullName = "GUSTAVO PEÑA CASTRO",
            Rut = "18.785.387-7",
            DestinationComuna = comuna!,
            CreatedByUserId = UserId
        });

    [Fact]
    public async Task OnPostSolicitar_DraftWithRegisteredContact_SendsAndMarksEnviada()
    {
        var fixture = BuildModel();
        fixture.ComunaContacts.Upsert(new ComunaContact { Comuna = "Quillota", Email = "contacto@muniquillota.cl" });
        var id = SeedDraft(fixture.Repository);

        var result = await fixture.Model.OnPostSolicitar(id);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(1, fixture.EmailSender.CallCount);
        Assert.Equal("contacto@muniquillota.cl", fixture.EmailSender.To);
        Assert.Equal(OutboundRequestStatus.Enviada, fixture.Repository.FindById(id)!.Status);
    }

    [Fact]
    public async Task OnPostSolicitar_NoRegisteredContact_KeepsItAsDraftAndDoesNotSend()
    {
        var fixture = BuildModel();
        var id = SeedDraft(fixture.Repository, comuna: "Comuna Sin Contacto");

        var result = await fixture.Model.OnPostSolicitar(id);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(0, fixture.EmailSender.CallCount);
        Assert.Equal(OutboundRequestStatus.Borrador, fixture.Repository.FindById(id)!.Status);
    }

    [Fact]
    public async Task OnPostSolicitar_AlreadySentRequest_DoesNotSendAgain()
    {
        var fixture = BuildModel();
        fixture.ComunaContacts.Upsert(new ComunaContact { Comuna = "Quillota", Email = "contacto@muniquillota.cl" });
        var id = SeedDraft(fixture.Repository);
        await fixture.Model.OnPostSolicitar(id);
        Assert.Equal(1, fixture.EmailSender.CallCount);

        var result = await fixture.Model.OnPostSolicitar(id);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(1, fixture.EmailSender.CallCount);
    }

    [Fact]
    public async Task OnPostSolicitar_UnknownId_DoesNotThrow()
    {
        var fixture = BuildModel();

        var result = await fixture.Model.OnPostSolicitar(999_999);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(0, fixture.EmailSender.CallCount);
    }
}
