namespace LicenciasCarpetas.Domain;

/// <summary>Attention point where the citation takes place. One agenda sheet per office and month.</summary>
public enum Office
{
    AvenidaArgentina,
    Placilla,
    MercadoPuerto
}

public static class OfficeCatalog
{
    public static string Display(Office office) => office switch
    {
        Office.AvenidaArgentina => "Av. Argentina",
        Office.Placilla => "Placilla",
        Office.MercadoPuerto => "Merc. Puerto",
        _ => office.ToString()
    };

    /// <summary>
    /// Resolves the office from free text: a sheet title ("MAYO AV. ARGENTINA"), the "ATENCIÓN"
    /// column ("SI, EN AV. ARGENTINA"), or an already-canonical enum name.
    /// </summary>
    public static Office? TryResolve(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var normalized = TextNormalizer.Normalize(text);

        // "MERC. PUERTO", "MERCADO PUERTO", "MERCADOPUERTO"
        if (normalized.Contains("MERC") && normalized.Contains("PUERTO"))
        {
            return Office.MercadoPuerto;
        }

        if (normalized.Contains("PLACILLA"))
        {
            return Office.Placilla;
        }

        if (normalized.Contains("ARGENTINA"))
        {
            return Office.AvenidaArgentina;
        }

        return null;
    }
}
