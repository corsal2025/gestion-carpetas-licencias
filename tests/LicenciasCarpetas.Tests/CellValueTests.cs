using LicenciasCarpetas.Import;

namespace LicenciasCarpetas.Tests;

public class CellValueTests
{
    [Fact]
    public void Reads_a_real_date_cell()
        => Assert.Equal(new DateOnly(2024, 9, 1), CellValue.ToDate(new DateTime(2024, 9, 1)));

    [Fact]
    public void Reads_an_excel_serial_number_as_a_date()
        => Assert.Equal(new DateOnly(2024, 9, 1), CellValue.ToDate(new DateTime(2024, 9, 1).ToOADate()));

    [Theory]
    [InlineData("15/03/2024")]
    [InlineData("15-03-2024")]
    [InlineData("15 marzo 2024")]
    [InlineData("15 de marzo de 2024")]
    public void Reads_the_date_formats_the_operator_types(string text)
        => Assert.Equal(new DateOnly(2024, 3, 15), CellValue.ToDate(text));

    [Theory]
    [InlineData("LIMACHE")]
    [InlineData("VIÑA DEL MAR")]
    [InlineData("")]
    [InlineData(null)]
    public void Text_that_is_not_a_date_stays_null(string? text)
        => Assert.Null(CellValue.ToDate(text));

    [Fact]
    public void Trims_text_and_treats_blanks_as_null()
    {
        Assert.Equal("LIMACHE", CellValue.ToText("  LIMACHE "));
        Assert.Null(CellValue.ToText("   "));
    }
}
