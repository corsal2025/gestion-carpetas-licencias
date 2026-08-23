using System.Net;
using LicenciasCarpetas.Tests.CambioDomicilio;

namespace LicenciasCarpetas.Tests.CambioDomicilio.Solicitar;

/// <summary>Same policy gate as the rest of the Cambio de Domicilio module
/// ("CambioDomicilioAccess"), now exercised against "Solicitar Cambios de Domicilio" — the
/// sidebar link hides the page for the wrong claim, but only this test proves the policy itself
/// refuses a direct URL hit too.</summary>
public class AccessControlTests : IClassFixture<CambioDomicilioWebAppFactory>
{
    private readonly CambioDomicilioWebAppFactory factory;

    public AccessControlTests(CambioDomicilioWebAppFactory factory) => this.factory = factory;

    [Fact]
    public async Task An_authorized_operator_reaches_the_solicitar_index_page()
    {
        using var client = factory.CreateAuthenticatedClient(canAccessCambioDomicilio: true);

        var response = await client.GetAsync("/CambioDomicilio/Solicitar/Index");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task An_operator_without_the_module_claim_is_refused_on_the_solicitar_index_page()
    {
        using var client = factory.CreateAuthenticatedClient(canAccessCambioDomicilio: false);

        var response = await client.GetAsync("/CambioDomicilio/Solicitar/Index");

        Assert.True(
            response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Redirect or HttpStatusCode.Found,
            $"expected 403 or a redirect, got {(int)response.StatusCode} {response.StatusCode}");
    }
}
