using System.Net;

namespace LicenciasCarpetas.Tests.CambioDomicilio;

/// <summary>Dashboard spec's Access Control requirement: the "CambioDomicilioAccess" policy
/// gates the page for real, not just the nav link that hides it.</summary>
public class CambioDomicilioAccessTests : IClassFixture<CambioDomicilioWebAppFactory>
{
    private readonly CambioDomicilioWebAppFactory factory;

    public CambioDomicilioAccessTests(CambioDomicilioWebAppFactory factory) => this.factory = factory;

    [Fact]
    public async Task An_authorized_operator_reaches_the_index_page()
    {
        using var client = factory.CreateAuthenticatedClient(canAccessCambioDomicilio: true);

        var response = await client.GetAsync("/CambioDomicilio/Index");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>Same shape as an F8-only operator: authenticated, but without the
    /// "mod:cambio-domicilio" claim — the policy must refuse without ever throwing.</summary>
    [Fact]
    public async Task An_operator_without_the_module_claim_is_refused_without_an_unhandled_exception()
    {
        using var client = factory.CreateAuthenticatedClient(canAccessCambioDomicilio: false);

        var response = await client.GetAsync("/CambioDomicilio/Index");

        Assert.True(
            response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Redirect or HttpStatusCode.Found,
            $"expected 403 or a redirect, got {(int)response.StatusCode} {response.StatusCode}");
    }
}
