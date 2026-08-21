using LicenciasCarpetas.CambioDomicilio.Domain;
using LicenciasCarpetas.CambioDomicilio.Ews;
using LicenciasCarpetas.CambioDomicilio.Reporting;
using LicenciasCarpetas.Dashboard.Auth;

namespace LicenciasCarpetas.Tests.CambioDomicilio.Routing;

/// <summary>Shared no-op stand-ins for AddressChangeRoutingService/CambioDomicilioSyncService's
/// dependencies that a given test doesn't exercise — mirrors FakeCambioDomicilioRequestRepository's
/// "orchestration, not persistence" scope, just for the smaller collaborators.</summary>
public sealed class NoopMailSender : IMailSender
{
    public Task SendAsync(string toAddress, string subject, string body, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class NoopEmailMover : IEmailMover
{
    public Task<bool> MoveAndMarkUnreadAsync(string messageId, string sourceFolderDisplayName, string destinationFolderDisplayName, CancellationToken cancellationToken)
        => Task.FromResult(true);
}

public sealed class NoopCsvReportWriter : ICsvReportWriter
{
    public void Write(IReadOnlyList<PersonRequest> requests, string outputPath) { }
}

public sealed class NoopUserRepository : IUserRepository
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

/// <summary>Blocks GetMessagesInFolderAsync until <paramref name="gate"/> completes — used to hold a
/// RunCycleAsync call open long enough for a concurrent call's overlap-guard behavior to be
/// observed.</summary>
public sealed class BlockingEmailReader(Task gate) : IEmailReader
{
    public async Task<IReadOnlyList<IncomingEmail>> GetMessagesInFolderAsync(string folderDisplayName, CancellationToken cancellationToken)
    {
        await gate;
        return [];
    }
}
