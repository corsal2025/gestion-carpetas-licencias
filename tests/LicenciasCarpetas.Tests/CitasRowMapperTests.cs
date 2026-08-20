using LicenciasCarpetas.Domain;
using LicenciasCarpetas.Import;

namespace LicenciasCarpetas.Tests;

public class CitasRowMapperTests
{
    private static RawCitasRow Row(
        object? citation = null,
        object? rut = null,
        object? fullName = null,
        object? email = null,
        object? cellPhone = null,
        object? tramite = null,
        object? ubicacion = null)
        => new(citation, rut, fullName, email, cellPhone, tramite, ubicacion);

    [Fact]
    public void Maps_a_complete_row()
    {
        var mapped = CitasRowMapper.Map(
            Row(citation: new DateTime(2026, 8, 20),
                rut: "13.025.150-1",
                fullName: "Juan Esteban Villagra Silva",
                email: "juan@correo.cl",
                cellPhone: 56950648787,
                tramite: "Primera vez, Extensión",
                ubicacion: "Av. Argentina"),
            rowNumber: 2,
            sourceSheet: "citas_20260819_143008");

        Assert.NotNull(mapped);
        Assert.Equal(new DateOnly(2026, 8, 20), mapped.CitationDate);
        Assert.Equal("13.025.150-1", mapped.Rut);
        Assert.Equal("JUAN ESTEBAN VILLAGRA SILVA", mapped.FullName);
        Assert.Equal("juan@correo.cl", mapped.Email);
        Assert.Equal("56950648787", mapped.CellPhone);
        Assert.Equal(Office.AvenidaArgentina, mapped.Office);
        Assert.Equal("citas_20260819_143008", mapped.SourceSheet);
        Assert.Equal(2, mapped.SourceRow);
        Assert.False(mapped.NeedsReview);
    }

    [Theory]
    [InlineData("Placilla", Office.Placilla)]
    [InlineData("Merc. Puerto", Office.MercadoPuerto)]
    [InlineData("Mercado Puerto", Office.MercadoPuerto)]
    public void Resolves_office_from_ubicacion(string ubicacion, Office expected)
    {
        var mapped = CitasRowMapper.Map(
            Row(citation: new DateTime(2026, 8, 20), rut: "13.025.150-1", fullName: "Juan Perez", ubicacion: ubicacion),
            rowNumber: 2,
            sourceSheet: "citas");

        Assert.Equal(expected, mapped!.Office);
    }

    [Fact]
    public void Extracts_a_licence_class_when_it_appears_in_the_tramite_text()
    {
        var mapped = CitasRowMapper.Map(
            Row(citation: new DateTime(2026, 8, 20), rut: "13.025.150-1", fullName: "Juan Perez",
                tramite: "Renovacion clase B"),
            rowNumber: 2,
            sourceSheet: "citas");

        Assert.Equal("B", mapped!.LicenceClasses);
    }

    [Fact]
    public void Leaves_licence_classes_null_when_the_tramite_text_names_none()
    {
        var mapped = CitasRowMapper.Map(
            Row(citation: new DateTime(2026, 8, 20), rut: "13.025.150-1", fullName: "Juan Perez",
                tramite: "Primera vez"),
            rowNumber: 2,
            sourceSheet: "citas");

        Assert.Null(mapped!.LicenceClasses);
    }

    [Fact]
    public void Missing_office_or_date_flags_the_row_for_review()
    {
        var mapped = CitasRowMapper.Map(
            Row(rut: "13.025.150-1", fullName: "Juan Perez"),
            rowNumber: 2,
            sourceSheet: "citas");

        Assert.True(mapped!.NeedsReview);
    }

    [Fact]
    public void A_row_without_any_person_is_skipped()
        => Assert.Null(CitasRowMapper.Map(Row(citation: new DateTime(2026, 8, 20)), rowNumber: 2, sourceSheet: "citas"));

    [Fact]
    public void An_invalid_rut_is_kept_verbatim_and_flagged_for_review()
    {
        var mapped = CitasRowMapper.Map(
            Row(citation: new DateTime(2026, 8, 20), rut: "13.025.150-9", fullName: "Juan Perez", ubicacion: "Placilla"),
            rowNumber: 2,
            sourceSheet: "citas");

        Assert.Equal("13.025.150-9", mapped!.Rut);
        Assert.True(mapped.NeedsReview);
    }
}
