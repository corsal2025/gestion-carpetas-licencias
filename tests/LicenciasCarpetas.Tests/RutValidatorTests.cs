using LicenciasCarpetas.Domain;

namespace LicenciasCarpetas.Tests;

public class RutValidatorTests
{
    [Theory]
    [InlineData("13.025.150-1", "13.025.150-1")]
    [InlineData("130251501", "13.025.150-1")]
    [InlineData("16.487.222-k", "16.487.222-K")]
    [InlineData("5.667.048-3", "05.667.048-3")]
    public void Normalizes_valid_ruts_to_dotted_form(string input, string expected)
        => Assert.Equal(expected, RutValidator.NormalizeAndValidate(input));

    [Theory]
    [InlineData("13.025.150-9")]
    [InlineData("abc")]
    [InlineData("1")]
    [InlineData("")]
    [InlineData(null)]
    public void Rejects_anything_that_is_not_a_valid_rut(string? input)
        => Assert.Null(RutValidator.NormalizeAndValidate(input));
}
