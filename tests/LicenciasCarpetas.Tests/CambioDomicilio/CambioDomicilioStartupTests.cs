using System.Net;
using LicenciasCarpetas.CambioDomicilio;
using Microsoft.Extensions.Configuration;

namespace LicenciasCarpetas.Tests.CambioDomicilio;

/// <summary>
/// Routing spec's Configuration Section requirement: <c>CambioDomicilioOptions</c> is entirely
/// optional (see <c>CambioDomicilioOptions.cs</c>'s comment and <c>Program.cs</c>'s
/// <c>?? new CambioDomicilioOptions()</c>), so a fresh install with no "CambioDomicilio:" section
/// in appsettings must still start and serve every other module.
///
/// <see cref="CambioDomicilioOptions_binding_returns_null_when_the_section_is_entirely_absent"/>
/// below proves the exact contract the "??" fallback in Program.cs relies on, using a bare
/// <see cref="ConfigurationBuilder"/> with no CambioDomicilio key at all.
///
/// The <see cref="CambioDomicilioWebAppFactory"/>-based tests below prove a different, complementary
/// thing: the real appsettings.json bundled with the app always ships a populated "CambioDomicilio"
/// section, and <c>IWebHostBuilder.UseSetting</c> can only overlay/blank existing leaf keys, not
/// remove the section outright (removing it would require replacing the whole host configuration
/// pipeline, which is a lot of fragile plumbing for no extra coverage over the direct binding test
/// above). So these tests exercise "every CambioDomicilio value is blank/degenerate" rather than
/// "the section is truly absent" — still a fully legitimate scenario (a badly-templated appsettings
/// on a fresh install), just not the literal absent-section case its name might suggest.
/// </summary>
public class CambioDomicilioStartupTests : IDisposable
{
    /// <summary>The actual guarantee Program.cs's <c>?? new CambioDomicilioOptions()</c> depends on:
    /// binding a section that no provider defines any key under returns null, not a degenerate
    /// object with empty-string properties.</summary>
    [Fact]
    public void CambioDomicilioOptions_binding_returns_null_when_the_section_is_entirely_absent()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Carpetas:SqliteDbPath"] = "carpetas.db" })
            .Build();

        var options = configuration.GetSection(CambioDomicilioOptions.SectionName).Get<CambioDomicilioOptions>();

        Assert.Null(options);
    }

    private static readonly Dictionary<string, string?> NoCambioDomicilioSection = new()
    {
        ["CambioDomicilio:Ews:Url"] = "",
        ["CambioDomicilio:Ews:Username"] = "",
        ["CambioDomicilio:Ews:Password"] = "",
        ["CambioDomicilio:MailboxAddress"] = "",
        ["CambioDomicilio:OwnDomain"] = "",
        ["CambioDomicilio:SourceFolderName"] = "",
        ["CambioDomicilio:ConfirmationFolderName"] = "",
        ["CambioDomicilio:ComunaDirectoryCsvPath"] = "",
        ["CambioDomicilio:ReportCsvPath"] = "",
        ["CambioDomicilio:NotificationEmailAddress"] = "",
        ["CambioDomicilio:CertificateRequestEmailAddress"] = "",
    };

    private readonly CambioDomicilioWebAppFactory factory = new() { ExtraSettings = NoCambioDomicilioSection };

    [Fact]
    public async Task The_app_starts_and_serves_login_with_no_CambioDomicilio_section_configured()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/Login");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>Casos (the base module, no per-module claim) still works — startup wiring for the
    /// unconfigured CambioDomicilio module did not break the rest of the DI container.</summary>
    [Fact]
    public async Task Casos_still_works_with_no_CambioDomicilio_section_configured()
    {
        using var client = factory.CreateAuthenticatedClient(canAccessCambioDomicilio: true);

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>F8's own policy still enforces correctly (a claim-less request is refused, not a
    /// 500) — proves F8's registrations were not corrupted by CambioDomicilio's missing config.</summary>
    [Fact]
    public async Task F8_module_is_unaffected_by_the_missing_CambioDomicilio_section()
    {
        using var client = factory.CreateAuthenticatedClient(canAccessCambioDomicilio: true); // no mod:f8-urgentes claim

        var response = await client.GetAsync("/F8/Index");

        Assert.True(
            response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Redirect or HttpStatusCode.Found,
            $"expected the F8Access policy to refuse cleanly (403/redirect), got {(int)response.StatusCode} {response.StatusCode}");
    }

    public void Dispose() => factory.Dispose();
}
