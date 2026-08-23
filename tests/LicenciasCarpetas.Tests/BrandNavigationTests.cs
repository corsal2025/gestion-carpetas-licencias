using LicenciasCarpetas.Dashboard.Pages.Shared;
using Microsoft.AspNetCore.Http;

namespace LicenciasCarpetas.Tests;

public class BrandNavigationTests
{
    [Theory]
    [InlineData("/", "Gestión de Licencias", false, false, true, "/Estadisticas")]
    [InlineData("/Index", "Gestión de Licencias", false, false, true, "/Estadisticas")]
    [InlineData("/Estadisticas", "Gestión de Licencias", false, false, true, "/Estadisticas")]
    [InlineData("/CambioDomicilio/Index", "Cambio de Domicilio", true, false, false, "/CambioDomicilio/Estadisticas")]
    [InlineData("/CambioDomicilio/Comunas", "Cambio de Domicilio", true, false, false, "/CambioDomicilio/Estadisticas")]
    [InlineData("/F8/Index", "F8 Urgentes", false, true, false, "/F8/Estadisticas")]
    [InlineData("/F8/Review", "F8 Urgentes", false, true, false, "/F8/Estadisticas")]
    public void Resolve_PicksTheCorrectModuleForThePath(
        string path, string expectedTitle, bool expectedCambioDomicilio, bool expectedF8, bool expectedGestionLicencias,
        string expectedEstadisticasPage)
    {
        var nav = BrandNavigation.Resolve(new PathString(path));

        Assert.Equal(expectedTitle, nav.Title);
        Assert.Equal(expectedCambioDomicilio, nav.EnCambioDomicilio);
        Assert.Equal(expectedF8, nav.EnF8);
        Assert.Equal(expectedGestionLicencias, nav.EnGestionLicencias);
        Assert.Equal(expectedEstadisticasPage, nav.EstadisticasPage);
    }

    /// <summary>Solicitar vive bajo el mismo prefijo /CambioDomicilio que Enviar — sin este flag
    /// aparte, el sidebar no podía distinguir cuál de los dos links marcar activo.</summary>
    [Theory]
    [InlineData("/CambioDomicilio/Index", false)]
    [InlineData("/CambioDomicilio/Solicitar/Index", true)]
    [InlineData("/CambioDomicilio/Solicitar/Nueva", true)]
    public void Resolve_DistinguishesSolicitarFromTheRestOfCambioDomicilio(string path, bool expectedSolicitar)
    {
        var nav = BrandNavigation.Resolve(new PathString(path));

        Assert.True(nav.EnCambioDomicilio);
        Assert.Equal(expectedSolicitar, nav.EnCambioDomicilioSolicitar);
    }

    /// <summary>StartsWithSegments requires a segment boundary — a route that merely starts with
    /// the same letters (but isn't actually under that module) must not false-positive.</summary>
    [Theory]
    [InlineData("/F8Reportes")]
    [InlineData("/CambioDomicilioX")]
    public void Resolve_DoesNotFalsePositiveOnAPrefixThatIsNotASegmentBoundary(string path)
    {
        var nav = BrandNavigation.Resolve(new PathString(path));

        Assert.Equal("Gestión de Licencias", nav.Title);
        Assert.True(nav.EnGestionLicencias);
        Assert.False(nav.EnCambioDomicilio);
        Assert.False(nav.EnF8);
    }
}
