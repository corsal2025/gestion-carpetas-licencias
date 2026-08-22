using System.Net;
using LicenciasCarpetas.Tests.CambioDomicilio;

namespace LicenciasCarpetas.Tests;

/// <summary>The sidebar's "Panel de Control" link (added ahead of this page) points at /Inicio —
/// this just confirms the route it links to actually renders instead of 404ing.</summary>
public class InicioAccessTests : IClassFixture<CambioDomicilioWebAppFactory>
{
    private readonly CambioDomicilioWebAppFactory factory;

    public InicioAccessTests(CambioDomicilioWebAppFactory factory) => this.factory = factory;

    [Fact]
    public async Task An_authenticated_user_reaches_the_hub_page()
    {
        using var client = factory.CreateAuthenticatedClient(canAccessCambioDomicilio: true);

        var response = await client.GetAsync("/Inicio");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Panel de Control", body);
    }
}
