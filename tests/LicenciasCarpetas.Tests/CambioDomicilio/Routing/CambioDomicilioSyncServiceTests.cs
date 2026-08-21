using LicenciasCarpetas.CambioDomicilio;
using LicenciasCarpetas.CambioDomicilio.Data;
using LicenciasCarpetas.CambioDomicilio.Directories;
using LicenciasCarpetas.CambioDomicilio.Domain;
using LicenciasCarpetas.CambioDomicilio.Ews;
using LicenciasCarpetas.CambioDomicilio.Reporting;
using LicenciasCarpetas.CambioDomicilio.Routing;
using LicenciasCarpetas.Dashboard.Auth;
using Microsoft.Extensions.Logging.Abstractions;

namespace LicenciasCarpetas.Tests.CambioDomicilio.Routing;

public class CambioDomicilioSyncServiceTests
{
    private static CambioDomicilioSyncService BuildService(
        IEmailReader emailReader, FakeCambioDomicilioRequestRepository repository, out AddressChangeRoutingService routingService,
        string comunaDirectoryCsvPath = "")
    {
        var options = new CambioDomicilioOptions
        {
            OwnDomain = "munivalpo.cl",
            // Empty by default: LoadDirectory() then returns [] rather than throwing, and
            // RunCycleAsync short-circuits before ever reading mail — a real path is only
            // needed by tests that must reach the email-reading step.
            ComunaDirectoryCsvPath = comunaDirectoryCsvPath
        };
        routingService = new AddressChangeRoutingService(
            repository,
            new FakeDiscardedEmailRepository(),
            new ComunaDirectory(),
            new NoopMailSender(),
            new NoopEmailMover(),
            new NoopUserRepository(),
            [],
            options,
            NullLogger<AddressChangeRoutingService>.Instance);

        return new CambioDomicilioSyncService(
            routingService,
            emailReader,
            repository,
            new NoopCsvReportWriter(),
            options,
            NullLogger<CambioDomicilioSyncService>.Instance);
    }

    /// <summary>The routing spec's overlap-prevention scenario: a cycle already in flight must make
    /// a concurrent call return immediately (false) instead of running two cycles at once.</summary>
    [Fact]
    public async Task RunCycleAsync_SecondCallWhileFirstStillRunning_ReturnsFalseWithoutRunning()
    {
        var csvPath = WriteComunaCsv();
        try
        {
            var gate = new TaskCompletionSource();
            var repository = new FakeCambioDomicilioRequestRepository();
            var service = BuildService(new BlockingEmailReader(gate.Task), repository, out _, csvPath);

            var firstCycle = service.RunCycleAsync(CancellationToken.None);
            var secondCycle = await service.RunCycleAsync(CancellationToken.None); // must not block waiting for the first

            Assert.False(secondCycle);

            gate.SetResult();
            var firstResult = await firstCycle;
            Assert.True(firstResult);
        }
        finally
        {
            File.Delete(csvPath);
        }
    }

    private static string WriteComunaCsv()
    {
        var path = Path.Combine(Path.GetTempPath(), $"comunas-test-{Guid.NewGuid():N}.csv");
        File.WriteAllLines(path, ["Comuna,ContactEmail,Domain", "Catemu,rfloresc@municatemu.cl,municatemu.cl"]);
        return path;
    }

    [Fact]
    public async Task RunCycleAsync_AfterPreviousCycleFinished_RunsAgain()
    {
        var repository = new FakeCambioDomicilioRequestRepository();
        var service = BuildService(new EmptyEmailReader(), repository, out _);

        var first = await service.RunCycleAsync(CancellationToken.None);
        var second = await service.RunCycleAsync(CancellationToken.None);

        Assert.True(first);
        Assert.True(second); // the guard released after the first cycle, so this is a real run, not a skip
    }

    [Fact]
    public async Task RunCycleAsync_EmptyDirectory_SkipsTheCycleWithoutReadingMail()
    {
        var reader = new CountingEmailReader();
        var repository = new FakeCambioDomicilioRequestRepository();
        var service = BuildService(reader, repository, out _);

        var ran = await service.RunCycleAsync(CancellationToken.None);

        Assert.True(ran); // "ran" means "did not skip due to overlap" — an empty directory is still a completed cycle
        Assert.Equal(0, reader.CallCount); // never got to reading mail: nowhere to route it to
    }

    private sealed class BlockingEmailReader(Task gate) : IEmailReader
    {
        public async Task<IReadOnlyList<IncomingEmail>> GetMessagesInFolderAsync(string folderDisplayName, CancellationToken cancellationToken)
        {
            await gate;
            return [];
        }
    }

    private sealed class EmptyEmailReader : IEmailReader
    {
        public Task<IReadOnlyList<IncomingEmail>> GetMessagesInFolderAsync(string folderDisplayName, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<IncomingEmail>>([]);
    }

    private sealed class CountingEmailReader : IEmailReader
    {
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<IncomingEmail>> GetMessagesInFolderAsync(string folderDisplayName, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult<IReadOnlyList<IncomingEmail>>([]);
        }
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

    private sealed class NoopCsvReportWriter : ICsvReportWriter
    {
        public void Write(IReadOnlyList<PersonRequest> requests, string outputPath) { }
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
