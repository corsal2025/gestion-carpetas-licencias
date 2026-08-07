using LicenciasCarpetas.Domain;
using LicenciasCarpetas.Import;

namespace LicenciasCarpetas.Tests;

public class AgendaSheetTests
{
    [Theory]
    [InlineData("ENERO AV. ARGENTINA", Office.AvenidaArgentina, 1)]
    [InlineData("MAYO PLACILLA", Office.Placilla, 5)]
    [InlineData("JUNIO MERC. PUERTO", Office.MercadoPuerto, 6)]
    public void Parses_office_and_month_from_the_sheet_title(string title, Office office, int month)
    {
        var sheet = AgendaSheet.TryParse(title);

        Assert.NotNull(sheet);
        Assert.Equal(office, sheet.Office);
        Assert.Equal(month, sheet.Month);
    }

    [Fact]
    public void Half_year_sheets_have_no_single_month()
    {
        var sheet = AgendaSheet.TryParse("2DO SEM. MERC. PUERTO");

        Assert.NotNull(sheet);
        Assert.Equal(Office.MercadoPuerto, sheet.Office);
        Assert.Null(sheet.Month);
    }

    [Theory]
    [InlineData("PLANTILLA MODELO AV. ARGENTINA")]
    [InlineData("ESCANEADAS Y SUBIDAS")]
    [InlineData("CORREOS CAMBIO DE DOMICLIO")]
    [InlineData("")]
    [InlineData(null)]
    public void Non_agenda_sheets_are_rejected(string? title)
        => Assert.Null(AgendaSheet.TryParse(title));
}
