using LicenciasCarpetas.Domain;

namespace LicenciasCarpetas.Tests;

/// <summary>
/// Three states were retired from the dropdowns at the operator's request. Retired means "cannot be
/// chosen from now on", not "deleted": 35 cases in the 2026 workbook already carry them and have to
/// keep showing what they say.
/// </summary>
public class RetiredFolderStateTests
{
    private static readonly FolderState[] Retired =
    [
        FolderState.SeEncuentraEnArchivos,
        FolderState.SeEncuentraEnOficina43,
        FolderState.CrearOficio
    ];

    [Theory]
    [InlineData(FolderState.SeEncuentraEnArchivos)]
    [InlineData(FolderState.SeEncuentraEnOficina43)]
    [InlineData(FolderState.CrearOficio)]
    public void A_retired_state_is_no_longer_offered(FolderState state)
        => Assert.DoesNotContain(state, FolderStateCatalog.Selectable);

    [Theory]
    [InlineData(FolderState.SubidaAConaset)]
    [InlineData(FolderState.PrimeraLicencia)]
    [InlineData(FolderState.CrearCertificado)]
    [InlineData(FolderState.CanjeLicenciaExtranjera)]
    public void The_rest_stays_available(FolderState state)
        => Assert.Contains(state, FolderStateCatalog.Selectable);

    [Fact]
    public void Retired_states_are_still_part_of_the_catalog()
    {
        foreach (var state in Retired)
        {
            Assert.Contains(state, FolderStateCatalog.All);
            Assert.False(string.IsNullOrEmpty(FolderStateCatalog.Display(state)));
        }
    }

    /// <summary>An import of the 2026 workbook still has to read these values, or 35 rows would
    /// come in as unreadable text and land in "requiere revisión".</summary>
    [Theory]
    [InlineData("SE ENCUENTRA EN ARCHIVOS", FolderState.SeEncuentraEnArchivos)]
    [InlineData("SE ENCUENTRA EN OF.43", FolderState.SeEncuentraEnOficina43)]
    [InlineData("CREAR OFICIO ", FolderState.CrearOficio)]
    public void Retired_states_are_still_recognised_when_importing(string text, FolderState expected)
        => Assert.Equal(expected, FolderStateCatalog.TryResolve(text));

    [Fact]
    public void Retired_states_keep_their_colour()
    {
        foreach (var state in Retired)
        {
            Assert.NotNull(FolderStateCatalog.Color(state));
        }
    }

    [Fact]
    public void The_catalog_reports_which_states_are_retired()
    {
        foreach (var state in Retired)
        {
            Assert.True(FolderStateCatalog.IsRetired(state));
        }

        Assert.False(FolderStateCatalog.IsRetired(FolderState.SubidaAConaset));
    }
}
