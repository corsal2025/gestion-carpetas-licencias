using LicenciasCarpetas.CambioDomicilio;
using LicenciasCarpetas.CambioDomicilio.Directories;
using LicenciasCarpetas.CambioDomicilio.Domain;
using LicenciasCarpetas.CambioDomicilio.Routing;
using LicenciasCarpetas.CambioDomicilio.Solicitar;
using LicenciasCarpetas.Dashboard.Pages.CambioDomicilio.Solicitar;
using LicenciasCarpetas.Domain;
using LicenciasCarpetas.F8.Services;
using LicenciasCarpetas.Persistence;
using LicenciasCarpetas.Tests.CambioDomicilio.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;

namespace LicenciasCarpetas.Tests.CambioDomicilio.Solicitar;

/// <summary>Exercises NuevaModel directly (no HTTP) against a fake outbound-request repository,
/// a fake comuna-contact repository and a recording email sender — covers the guard rails around
/// saving a draft and sending it (no matching contact, and the happy path).</summary>
public class NuevaModelTests
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
        public string? Subject { get; private set; }
        public string? Body { get; private set; }
        public IReadOnlyList<EmailAttachment>? Attachments { get; private set; }
        public int CallCount { get; private set; }

        public Task SendAsync(string to, string subject, string body,
            IReadOnlyList<EmailAttachment>? attachments = null, CancellationToken cancellationToken = default)
        {
            CallCount++;
            To = to;
            Subject = subject;
            Body = body;
            Attachments = attachments;
            return Task.CompletedTask;
        }
    }

    private sealed record Fixture(
        NuevaModel Model,
        FakeOutboundAddressChangeRequestRepository Repository,
        FakeComunaContactRepository ComunaContacts,
        RecordingEmailSender EmailSender);

    private Fixture BuildModel()
    {
        var repository = new FakeOutboundAddressChangeRequestRepository();
        var comunaContacts = new FakeComunaContactRepository();
        var emailSender = new RecordingEmailSender();

        var routingService = new AddressChangeRoutingService(
            new FakeCambioDomicilioRequestRepository(),
            new FakeDiscardedEmailRepository(),
            new ComunaDirectory(),
            new NoopMailSender(),
            new NoopEmailMover(),
            new NoopUserRepository(),
            [],
            new CambioDomicilioOptions(),
            NullLogger<AddressChangeRoutingService>.Instance);

        var sender = new OutboundRequestSender(repository, comunaContacts, emailSender);

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, UserId.ToString())], "Test"))
        };

        var model = new NuevaModel(repository, routingService, sender)
        {
            PageContext = new PageContext(new ActionContext(httpContext, new RouteData(), new PageActionDescriptor())),
            TempData = new TempDataDictionary(httpContext, new InMemoryTempDataProvider())
        };

        return new Fixture(model, repository, comunaContacts, emailSender);
    }

    private static NuevaModel.InputModel ValidInput() => new()
    {
        FullName = "GUSTAVO PEÑA CASTRO",
        Rut = "18.785.387-7",
        Street = "Av. Argentina",
        Number = "123",
        DestinationComuna = "Catemu"
    };

    [Fact]
    public void OnPostGuardarBorrador_ValidInput_CreatesDraft()
    {
        var fixture = BuildModel();
        fixture.Model.Input = ValidInput();

        var result = fixture.Model.OnPostGuardarBorrador(null);

        Assert.IsType<RedirectToPageResult>(result);
        var stored = Assert.Single(fixture.Repository.GetAll());
        Assert.Equal(OutboundRequestStatus.Borrador, stored.Status);
        Assert.Equal("18.785.387-7", stored.Rut);
    }

    [Fact]
    public void OnPostGuardarBorrador_InvalidRut_RejectsAndDoesNotInsert()
    {
        var fixture = BuildModel();
        fixture.Model.Input = ValidInput();
        fixture.Model.Input.Rut = "11.111.111-2"; // correct check digit is 1, not 2

        var result = fixture.Model.OnPostGuardarBorrador(null);

        Assert.IsType<PageResult>(result);
        Assert.True(fixture.Model.MessageIsError);
        Assert.Empty(fixture.Repository.GetAll());
    }

    [Fact]
    public async Task OnPostEnviar_NoMatchingContact_BlocksSend()
    {
        var fixture = BuildModel();
        fixture.Model.Input = ValidInput();
        fixture.Model.OnPostGuardarBorrador(null);
        var id = fixture.Repository.GetAll().Single().Id;

        var result = await fixture.Model.OnPostEnviar(id);

        Assert.IsType<PageResult>(result);
        Assert.True(fixture.Model.MessageIsError);
        Assert.Contains("No hay correo de contacto registrado", fixture.Model.Message);
        Assert.Equal(0, fixture.EmailSender.CallCount);
        Assert.Equal(OutboundRequestStatus.Borrador, fixture.Repository.FindById(id)!.Status);
    }

    [Fact]
    public async Task OnPostEnviar_MatchingContact_SendsAndMarksSent()
    {
        var fixture = BuildModel();
        fixture.Model.Input = ValidInput();
        fixture.Model.OnPostGuardarBorrador(null);
        var id = fixture.Repository.GetAll().Single().Id;
        fixture.ComunaContacts.Upsert(new ComunaContact { Comuna = "Catemu", Email = "contacto@municatemu.cl" });

        var result = await fixture.Model.OnPostEnviar(id);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(1, fixture.EmailSender.CallCount);
        Assert.Equal("contacto@municatemu.cl", fixture.EmailSender.To);
        Assert.NotNull(fixture.EmailSender.Attachments);
        Assert.Empty(fixture.EmailSender.Attachments!);

        var stored = fixture.Repository.FindById(id)!;
        Assert.Equal(OutboundRequestStatus.Enviada, stored.Status);
        Assert.NotNull(stored.SentAt);
        Assert.Equal(UserId, stored.SentByUserId);
    }

    [Fact]
    public async Task OnPostEnviar_BlankStreetAndNumber_OmitsAddressLineInsteadOfGarbling()
    {
        var fixture = BuildModel();
        fixture.Model.Input = ValidInput();
        fixture.Model.Input.Street = null;
        fixture.Model.Input.Number = null;
        fixture.Model.OnPostGuardarBorrador(null);
        var id = fixture.Repository.GetAll().Single().Id;
        fixture.ComunaContacts.Upsert(new ComunaContact { Comuna = "Catemu", Email = "contacto@municatemu.cl" });

        var result = await fixture.Model.OnPostEnviar(id);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.NotNull(fixture.EmailSender.Body);
        Assert.DoesNotContain("Nuevo domicilio:", fixture.EmailSender.Body);
    }
}
