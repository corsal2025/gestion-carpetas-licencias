using LicenciasCarpetas.CambioDomicilio;
using LicenciasCarpetas.CambioDomicilio.Data;
using LicenciasCarpetas.CambioDomicilio.Directories;
using LicenciasCarpetas.CambioDomicilio.Domain;
using LicenciasCarpetas.CambioDomicilio.Ews;
using LicenciasCarpetas.CambioDomicilio.Notifications;
using LicenciasCarpetas.CambioDomicilio.Routing;
using LicenciasCarpetas.Dashboard.Auth;
using Microsoft.Extensions.Logging.Abstractions;

namespace LicenciasCarpetas.Tests.CambioDomicilio.Routing;

public class AddressChangeRoutingServiceTests
{
    private static readonly IReadOnlyList<ComunaRoutingEntry> Contacts =
        [new ComunaRoutingEntry("Catemu", "rfloresc@municatemu.cl", "municatemu.cl")];

    private static AddressChangeRoutingService BuildService(
        ICambioDomicilioRequestRepository repository,
        IDiscardedEmailRepository discardedRepository,
        CambioDomicilioOptions? options = null)
        => new(
            repository,
            discardedRepository,
            new ComunaDirectory(),
            new NoopMailSender(),
            new NoopEmailMover(),
            new NoopUserRepository(),
            [],
            options ?? new CambioDomicilioOptions { OwnDomain = "munivalpo.cl" },
            NullLogger<AddressChangeRoutingService>.Instance);

    private static IncomingEmail Email(string messageId = "msg-1", string sender = "rfloresc@municatemu.cl") => new(
        MessageId: messageId,
        ConversationId: "conv-1",
        Subject: "Cambio de domicilio",
        SenderAddress: sender,
        BodyText: "GUSTAVO PEÑA CASTRO RUT: 18.785.387-7",
        ReceivedAt: DateTimeOffset.UtcNow);

    [Fact]
    public void ProcessIncomingRequest_RecognizedDomain_RegistersPendingCase()
    {
        var repository = new FakeCambioDomicilioRequestRepository();
        var service = BuildService(repository, new FakeDiscardedEmailRepository());

        service.ProcessIncomingRequest(Email(), Contacts);

        var stored = Assert.Single(repository.GetAll());
        Assert.Equal(RequestStatus.Pending, stored.Status);
        Assert.Equal("18.785.387-7", stored.Rut);
        Assert.Equal("Catemu", stored.Comuna);
        Assert.False(stored.NeedsReview);
    }

    [Fact]
    public void ProcessIncomingRequest_UnrecognizedDomain_DiscardsInsteadOfTracking()
    {
        var repository = new FakeCambioDomicilioRequestRepository();
        var discarded = new FakeDiscardedEmailRepository();
        var service = BuildService(repository, discarded);

        service.ProcessIncomingRequest(Email(sender: "alguien@dominio-no-registrado.cl"), Contacts);

        Assert.Empty(repository.GetAll());
        Assert.Single(discarded.GetAll());
    }

    [Fact]
    public void ProcessIncomingRequest_OwnDomain_IsIgnoredSilently()
    {
        var repository = new FakeCambioDomicilioRequestRepository();
        var discarded = new FakeDiscardedEmailRepository();
        var service = BuildService(repository, discarded);

        service.ProcessIncomingRequest(Email(sender: "alguien@munivalpo.cl"), Contacts);

        Assert.Empty(repository.GetAll());
        Assert.Empty(discarded.GetAll()); // internal mail is not routing-relevant, not even worth flagging
    }

    [Fact]
    public void ProcessIncomingRequest_SameMessageIdTwice_DoesNotDuplicate()
    {
        var repository = new FakeCambioDomicilioRequestRepository();
        var service = BuildService(repository, new FakeDiscardedEmailRepository());

        service.ProcessIncomingRequest(Email(), Contacts);
        service.ProcessIncomingRequest(Email(), Contacts); // same SourceMessageId, e.g. re-polled

        Assert.Single(repository.GetAll());
    }

    [Fact]
    public void ProcessUploadedCase_MarksThePendingRequestUploaded()
    {
        var repository = new FakeCambioDomicilioRequestRepository();
        var service = BuildService(repository, new FakeDiscardedEmailRepository());
        service.ProcessIncomingRequest(Email(), Contacts);

        service.ProcessUploadedCase(Email());

        Assert.Equal(RequestStatus.Uploaded, repository.GetAll()[0].Status);
    }

    private sealed class NoopMailSender : IMailSender
    {
        public Task SendAsync(string toAddress, string subject, string body, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NoopEmailMover : IEmailMover
    {
        public Task<bool> MoveAndMarkUnreadAsync(string messageId, string sourceFolderDisplayName, string destinationFolderDisplayName, CancellationToken cancellationToken)
            => Task.FromResult(true);
    }

    private sealed class NoopUserRepository : IUserRepository
    {
        public void EnsureSchema() { }
        public DashboardUser? FindByUsername(string username) => null;
        public DashboardUser? FindById(long id) => null;
        public void Insert(DashboardUser user) { }
        public void UpdatePassword(long id, string hash, string salt, int iterations) { }
        public void ResetPassword(long id, string hash, string salt, int iterations) { }
        public IReadOnlyList<string> AllUsernames() => [];
        public IReadOnlyList<DashboardUser> AllUsers() => [];
        public void Delete(string username) { }
        public void RecordFailedLogin(long id, int attempts, DateTimeOffset? lockedUntil) { }
        public void ResetFailedLogins(long id) { }
        public int Count() => 0;
        public void UpdateRole(long id, UserRole role, bool canAccessCambioDomicilio, bool canAccessF8Urgentes) { }
        public void UpdateEmailFooter(long id, string? footer) { }
    }
}
