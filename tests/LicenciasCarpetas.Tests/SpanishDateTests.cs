using LicenciasCarpetas.Domain;

namespace LicenciasCarpetas.Tests;

public class SpanishDateTests
{
    /// <summary>
    /// El documento que se lleva a Archivo escribe la fecha como número/palabra/número, para que
    /// no haya duda entre día y mes al leerla en papel.
    /// </summary>
    [Theory]
    [InlineData(2024, 3, 15, "15/marzo/2024")]
    [InlineData(2023, 7, 1, "1/julio/2023")]
    [InlineData(2009, 12, 31, "31/diciembre/2009")]
    [InlineData(2026, 1, 2, "2/enero/2026")]
    public void Formats_a_date_as_number_word_number(int year, int month, int day, string expected)
        => Assert.Equal(expected, SpanishDate.FormatWithMonthName(new DateOnly(year, month, day)));

    [Fact]
    public void An_empty_date_prints_as_a_dash()
        => Assert.Equal("—", SpanishDate.FormatWithMonthName(null));

    [Theory]
    [InlineData("15/marzo/2024", 2024, 3, 15)]
    [InlineData("1/julio/2023", 2023, 7, 1)]
    public void The_same_format_can_be_read_back(string text, int year, int month, int day)
    {
        Assert.True(SpanishDate.TryParse(text, out var parsed));
        Assert.Equal(new DateOnly(year, month, day), parsed);
    }
}
