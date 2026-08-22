using LicenciasCarpetas.CambioDomicilio;
using LicenciasCarpetas.CambioDomicilio.Directories;
using LicenciasCarpetas.CambioDomicilio.Domain;
using LicenciasCarpetas.CambioDomicilio.Routing;
using LicenciasCarpetas.Configuration;
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
/// saving a draft and sending it (no attachment, no matching contact, and the happy path).</summary>
public class NuevaModelTests : IDisposable
{
    private const long UserId = 42;
    private readonly string _uploadDirectory = Path.Combine(Path.GetTempPath(), $"licencias-carpetas-solicitar-{Guid.NewGuid():N}");

    public NuevaModelTests() => Directory.CreateDirectory(_uploadDirectory);

    public void Dispose()
    {
        if (Directory.Exists(_uploadDirectory))
        {
            Directory.Delete(_uploadDirectory, recursive: true);
        }
    }

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

    private sealed class FakeFormFile(byte[] content, string fileName, string contentType = "application/octet-stream", long? lengthOverride = null) : IFormFile
    {
        public string ContentType { get; } = contentType;
        public string ContentDisposition => $"form-data; name=\"file\"; filename=\"{fileName}\"";
        public IHeaderDictionary Headers { get; } = new HeaderDictionary();
        public long Length => lengthOverride ?? content.Length;
        public string Name => "file";
        public string FileName { get; } = fileName;

        public void CopyTo(Stream target) => target.Write(content, 0, content.Length);

        public Task CopyToAsync(Stream target, CancellationToken cancellationToken = default)
        {
            target.Write(content, 0, content.Length);
            return Task.CompletedTask;
        }

        public Stream OpenReadStream() => new MemoryStream(content);
    }

    private static readonly byte[] PdfBytes = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34, 0x0A, 0x25, 0x25, 0x45, 0x4F, 0x46];

    private static FakeFormFile ValidPdfFile(string fileName) => new(PdfBytes, fileName, "application/pdf");

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
        var carpetasOptions = new CarpetasOptions { UploadDirectory = _uploadDirectory };

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

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, UserId.ToString())], "Test"))
        };

        var model = new NuevaModel(repository, comunaContacts, routingService, emailSender, carpetasOptions)
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
    public async Task OnPostEnviar_NoAttachments_BlocksSend()
    {
        var fixture = BuildModel();
        fixture.Model.Input = ValidInput();
        fixture.Model.OnPostGuardarBorrador(null);
        var id = fixture.Repository.GetAll().Single().Id;

        var result = await fixture.Model.OnPostEnviar(id);

        Assert.IsType<PageResult>(result);
        Assert.True(fixture.Model.MessageIsError);
        Assert.Equal(0, fixture.EmailSender.CallCount);
        Assert.Equal(OutboundRequestStatus.Borrador, fixture.Repository.FindById(id)!.Status);
    }

    [Fact]
    public async Task OnPostEnviar_AttachmentButNoMatchingContact_BlocksSend()
    {
        var fixture = BuildModel();
        fixture.Model.Input = ValidInput();
        fixture.Model.OnPostGuardarBorrador(null);
        var id = fixture.Repository.GetAll().Single().Id;
        fixture.Repository.AddAttachment(new OutboundAddressChangeAttachment
        {
            RequestId = id,
            FileName = "documento.pdf",
            StoredPath = Path.Combine(_uploadDirectory, "documento.pdf"),
            ContentType = "application/pdf"
        });

        var result = await fixture.Model.OnPostEnviar(id);

        Assert.IsType<PageResult>(result);
        Assert.True(fixture.Model.MessageIsError);
        Assert.Contains("No hay correo de contacto registrado", fixture.Model.Message);
        Assert.Equal(0, fixture.EmailSender.CallCount);
        Assert.Equal(OutboundRequestStatus.Borrador, fixture.Repository.FindById(id)!.Status);
    }

    [Fact]
    public async Task OnPostEnviar_AttachmentAndMatchingContact_SendsAndMarksSent()
    {
        var fixture = BuildModel();
        fixture.Model.Input = ValidInput();
        fixture.Model.OnPostGuardarBorrador(null);
        var id = fixture.Repository.GetAll().Single().Id;

        var attachmentPath = Path.Combine(_uploadDirectory, "documento.pdf");
        File.WriteAllText(attachmentPath, "contenido de prueba");
        fixture.Repository.AddAttachment(new OutboundAddressChangeAttachment
        {
            RequestId = id,
            FileName = "documento.pdf",
            StoredPath = attachmentPath,
            ContentType = "application/pdf"
        });
        fixture.ComunaContacts.Upsert(new ComunaContact { Comuna = "Catemu", Email = "contacto@municatemu.cl" });

        var result = await fixture.Model.OnPostEnviar(id);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(1, fixture.EmailSender.CallCount);
        Assert.Equal("contacto@municatemu.cl", fixture.EmailSender.To);
        Assert.NotNull(fixture.EmailSender.Attachments);
        var attachment = Assert.Single(fixture.EmailSender.Attachments!);
        Assert.Equal("documento.pdf", attachment.FileName);

        var stored = fixture.Repository.FindById(id)!;
        Assert.Equal(OutboundRequestStatus.Enviada, stored.Status);
        Assert.NotNull(stored.SentAt);
        Assert.Equal(UserId, stored.SentByUserId);
    }

    [Fact]
    public async Task OnPostSubirArchivo_ValidPdfOnExistingDraft_AddsAttachment()
    {
        var fixture = BuildModel();
        fixture.Model.Input = ValidInput();
        fixture.Model.OnPostGuardarBorrador(null);
        var id = fixture.Repository.GetAll().Single().Id;

        fixture.Model.NewAttachment = ValidPdfFile("documento.pdf");
        var result = await fixture.Model.OnPostSubirArchivo(id);

        Assert.IsType<RedirectToPageResult>(result);
        var attachment = Assert.Single(fixture.Repository.GetAttachments(id));
        Assert.Equal("documento.pdf", attachment.FileName);
    }

    [Fact]
    public async Task OnPostSubirArchivo_NoExistingDraft_AutoCreatesDraftThenAttaches()
    {
        var fixture = BuildModel();
        fixture.Model.Input = ValidInput();
        fixture.Model.NewAttachment = ValidPdfFile("documento.pdf");

        var result = await fixture.Model.OnPostSubirArchivo(null);

        Assert.IsType<RedirectToPageResult>(result);
        var stored = Assert.Single(fixture.Repository.GetAll());
        var attachment = Assert.Single(fixture.Repository.GetAttachments(stored.Id));
        Assert.Equal("documento.pdf", attachment.FileName);
    }

    [Fact]
    public async Task OnPostSubirArchivo_WrongExtension_RejectsAndDoesNotAttach()
    {
        var fixture = BuildModel();
        fixture.Model.Input = ValidInput();
        fixture.Model.OnPostGuardarBorrador(null);
        var id = fixture.Repository.GetAll().Single().Id;

        fixture.Model.NewAttachment = new FakeFormFile(PdfBytes, "documento.txt");
        var result = await fixture.Model.OnPostSubirArchivo(id);

        Assert.IsType<PageResult>(result);
        Assert.True(fixture.Model.MessageIsError);
        Assert.Empty(fixture.Repository.GetAttachments(id));
    }

    [Fact]
    public async Task OnPostSubirArchivo_OversizedFile_RejectsAndDoesNotAttach()
    {
        var fixture = BuildModel();
        fixture.Model.Input = ValidInput();
        fixture.Model.OnPostGuardarBorrador(null);
        var id = fixture.Repository.GetAll().Single().Id;

        fixture.Model.NewAttachment = new FakeFormFile(PdfBytes, "documento.pdf", "application/pdf", lengthOverride: 6 * 1024 * 1024);
        var result = await fixture.Model.OnPostSubirArchivo(id);

        Assert.IsType<PageResult>(result);
        Assert.True(fixture.Model.MessageIsError);
        Assert.Empty(fixture.Repository.GetAttachments(id));
    }

    [Fact]
    public async Task OnPostSubirArchivo_PdfExtensionWrongSignature_RejectsAndDoesNotAttach()
    {
        var fixture = BuildModel();
        fixture.Model.Input = ValidInput();
        fixture.Model.OnPostGuardarBorrador(null);
        var id = fixture.Repository.GetAll().Single().Id;

        var fakeContent = System.Text.Encoding.ASCII.GetBytes("este no es un pdf real, solo texto plano");
        fixture.Model.NewAttachment = new FakeFormFile(fakeContent, "documento.pdf", "application/pdf");
        var result = await fixture.Model.OnPostSubirArchivo(id);

        Assert.IsType<PageResult>(result);
        Assert.True(fixture.Model.MessageIsError);
        Assert.Empty(fixture.Repository.GetAttachments(id));
    }

    [Fact]
    public async Task OnPostSubirArchivo_AlreadyEnviada_RejectsAndDoesNotMutate()
    {
        var fixture = BuildModel();
        fixture.Model.Input = ValidInput();
        fixture.Model.OnPostGuardarBorrador(null);
        var id = fixture.Repository.GetAll().Single().Id;
        fixture.Repository.MarkSent(id, DateTimeOffset.UtcNow, UserId);

        fixture.Model.NewAttachment = ValidPdfFile("documento.pdf");
        var result = await fixture.Model.OnPostSubirArchivo(id);

        Assert.IsType<PageResult>(result);
        Assert.True(fixture.Model.MessageIsError);
        Assert.Empty(fixture.Repository.GetAttachments(id));
    }

    [Fact]
    public void OnPostEliminarArchivo_AlreadyEnviada_RejectsAndDoesNotMutate()
    {
        var fixture = BuildModel();
        fixture.Model.Input = ValidInput();
        fixture.Model.OnPostGuardarBorrador(null);
        var id = fixture.Repository.GetAll().Single().Id;
        var attachmentId = fixture.Repository.AddAttachment(new OutboundAddressChangeAttachment
        {
            RequestId = id,
            FileName = "documento.pdf",
            StoredPath = Path.Combine(_uploadDirectory, "documento.pdf"),
            ContentType = "application/pdf"
        });
        fixture.Repository.MarkSent(id, DateTimeOffset.UtcNow, UserId);

        var result = fixture.Model.OnPostEliminarArchivo(id, attachmentId);

        Assert.IsType<PageResult>(result);
        Assert.True(fixture.Model.MessageIsError);
        Assert.Single(fixture.Repository.GetAttachments(id));
    }
}
