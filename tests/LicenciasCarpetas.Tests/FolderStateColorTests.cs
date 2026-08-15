using LicenciasCarpetas.Domain;

namespace LicenciasCarpetas.Tests;

/// <summary>
/// The workbook paints the whole row from the "ESTADO DE LA CARPETA" cell, and the operator reads
/// the agenda by those colours before reading any text. They are copied here exactly as the
/// conditional-formatting rules of the 2026 workbook define them.
/// </summary>
public class FolderStateColorTests
{
    [Theory]
    [InlineData(FolderState.PrimeraLicencia, "#FF00FF")]
    [InlineData(FolderState.SubidaAConaset, "#FFFF00")]
    [InlineData(FolderState.SubidaConF8, "#BF9000")]
    [InlineData(FolderState.SubidaConOficio, "#FFE599")]
    [InlineData(FolderState.CambioDomicilioSubidoAConaset, "#00FFFF")]
    [InlineData(FolderState.CambioDomicilioSubidoConCorreo, "#D0E0E3")]
    [InlineData(FolderState.CambioDomicilioSolicitado, "#9FC5E8")]
    [InlineData(FolderState.CambioDomicilio, "#3D85C6")]
    [InlineData(FolderState.SeEncuentraEnArchivos, "#6AA84F")]
    [InlineData(FolderState.SeEncuentraEnOficina43, "#8E7CC3")]
    [InlineData(FolderState.NoExisteCarpeta, "#FF0000")]
    [InlineData(FolderState.CrearOficio, "#C27BA0")]
    [InlineData(FolderState.CrearCertificado, "#C27BA0")]
    public void Every_state_keeps_the_colour_it_has_in_the_workbook(FolderState state, string expected)
        => Assert.Equal(expected, FolderStateCatalog.Color(state));

    /// <summary>The workbook has no rule for this one, so it must not invent a colour.</summary>
    [Fact]
    public void A_state_without_a_rule_in_the_workbook_has_no_colour()
        => Assert.Null(FolderStateCatalog.Color(FolderState.CanjeLicenciaExtranjera));

    [Fact]
    public void Every_catalog_value_is_accounted_for()
    {
        foreach (var state in FolderStateCatalog.All)
        {
            // Either it has a colour or it deliberately has none; what it cannot do is throw.
            var colour = FolderStateCatalog.Color(state);
            Assert.True(colour is null || colour.StartsWith('#'));
        }
    }

    /// <summary>The CSS class is what the page puts on the row; it has to be stable and safe.</summary>
    [Theory]
    [InlineData(FolderState.SubidaAConaset, "estado-subidaaconaset")]
    [InlineData(FolderState.CambioDomicilioSubidoConCorreo, "estado-cambiodomiciliosubidoconcorreo")]
    public void The_row_class_is_derived_from_the_state_name(FolderState state, string expected)
        => Assert.Equal(expected, FolderStateCatalog.CssClass(state));

    [Fact]
    public void There_is_no_row_class_without_a_state()
        => Assert.Equal(string.Empty, FolderStateCatalog.CssClass(null));
}
