using LicenciasCarpetas.Domain;

namespace LicenciasCarpetas.Tests;

/// <summary>
/// Un contribuyente puede sacar más de una clase a la vez (por ejemplo B y C, o A2 y A3), así que
/// la columna guarda una selección múltiple, no un valor único.
/// </summary>
public class LicenceClassTests
{
    [Fact]
    public void The_catalog_covers_the_chilean_classes()
    {
        var codes = LicenceClassCatalog.All.Select(LicenceClassCatalog.Display).ToArray();

        Assert.Equal(["A1", "A2", "A3", "A4", "A5", "B", "C", "D", "E", "F"], codes);
    }

    [Theory]
    [InlineData("B", new[] { LicenceClass.B })]
    [InlineData("B,C", new[] { LicenceClass.B, LicenceClass.C })]
    [InlineData("A2,A3", new[] { LicenceClass.A2, LicenceClass.A3 })]
    public void Reads_back_what_was_stored(string stored, LicenceClass[] expected)
        => Assert.Equal(expected, LicenceClassCatalog.Parse(stored));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public void No_selection_reads_as_an_empty_list(string? stored)
        => Assert.Empty(LicenceClassCatalog.Parse(stored));

    /// <summary>Un valor viejo o mal escrito no puede reventar la pantalla de casos.</summary>
    [Theory]
    [InlineData("B,ZZ", new[] { LicenceClass.B })]
    [InlineData("ZZ", new LicenceClass[0])]
    [InlineData("b , c ", new[] { LicenceClass.B, LicenceClass.C })]
    public void Unknown_codes_are_ignored(string stored, LicenceClass[] expected)
        => Assert.Equal(expected, LicenceClassCatalog.Parse(stored));

    [Fact]
    public void The_selection_is_stored_in_catalog_order_without_repeats()
    {
        var stored = LicenceClassCatalog.Serialize([LicenceClass.C, LicenceClass.B, LicenceClass.C]);

        Assert.Equal("B,C", stored);
    }

    [Fact]
    public void An_empty_selection_is_stored_as_nothing()
        => Assert.Null(LicenceClassCatalog.Serialize([]));

    [Fact]
    public void The_selection_reads_as_a_short_label()
    {
        Assert.Equal("B, C", LicenceClassCatalog.DisplayList("B,C"));
        Assert.Equal("—", LicenceClassCatalog.DisplayList(null));
    }

    /// <summary>Las clases profesionales exigen carpeta y controles distintos; el sistema tiene que
    /// poder separarlas para las estadísticas.</summary>
    [Theory]
    [InlineData(LicenceClass.A1, true)]
    [InlineData(LicenceClass.A5, true)]
    [InlineData(LicenceClass.B, false)]
    [InlineData(LicenceClass.F, false)]
    public void Knows_which_classes_are_professional(LicenceClass licence, bool professional)
        => Assert.Equal(professional, LicenceClassCatalog.IsProfessional(licence));
}
