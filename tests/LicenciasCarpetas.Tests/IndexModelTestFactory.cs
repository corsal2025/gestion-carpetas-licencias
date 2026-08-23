using LicenciasCarpetas.CambioDomicilio.Solicitar;
using LicenciasCarpetas.Configuration;
using LicenciasCarpetas.Dashboard.Pages;
using LicenciasCarpetas.F8.Services;
using LicenciasCarpetas.Reporting;
using LicenciasCarpetas.Tests.CambioDomicilio.Solicitar;

namespace LicenciasCarpetas.Tests;

/// <summary>Builds a Casos IndexModel wired to the same fakes every test that doesn't exercise
/// "Solicitar Cambio de Domicilio" still needs to satisfy the constructor — keeps that plumbing out
/// of tests whose actual subject is listing/paging/export/editing.</summary>
internal static class IndexModelTestFactory
{
    private sealed class NoopEmailSender : IEmailSender
    {
        public Task SendAsync(string to, string subject, string body,
            IReadOnlyList<EmailAttachment>? attachments = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    public static IndexModel Create(SqliteTestDatabase db, IExcelCaseExporter exporter, CarpetasOptions options)
    {
        var outboundRequests = new FakeOutboundAddressChangeRequestRepository();
        var sender = new OutboundRequestSender(outboundRequests, db.Contacts, new NoopEmailSender());
        return new(db.Cases, exporter, options, outboundRequests, sender);
    }
}
