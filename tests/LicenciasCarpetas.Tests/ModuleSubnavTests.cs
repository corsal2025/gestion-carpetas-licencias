using LicenciasCarpetas.Dashboard.Pages.Shared;
using Microsoft.AspNetCore.Http;

namespace LicenciasCarpetas.Tests;

public class ModuleSubnavTests
{
    [Theory]
    [InlineData("/F8/Index", "/F8/Index", true)]
    [InlineData("/F8/Index/", "/F8/Index", true)]
    [InlineData("/F8/Estadisticas", "/F8/Index", false)]
    [InlineData("/F8Reportes", "/F8", false)]
    public void IsActive_RequiresASegmentBoundary(string requestPath, string page, bool expected)
    {
        Assert.Equal(expected, ModuleSubnav.IsActive(new PathString(requestPath), page));
    }

    [Theory]
    [InlineData("/F8/SectorF8", "Archivo", "Archivo", true)]
    [InlineData("/F8/SectorF8", "Oficina43", "Archivo", false)]
    [InlineData("/F8/Index", "Archivo", "Archivo", false)]
    public void IsSectorActive_RequiresBothThePageAndTheSectorValue(
        string requestPath, string sector, string sectorValue, bool expected)
    {
        var result = ModuleSubnav.IsSectorActive(new PathString(requestPath), "/F8/SectorF8", sector, sectorValue);

        Assert.Equal(expected, result);
    }
}
