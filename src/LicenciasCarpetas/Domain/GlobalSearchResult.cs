namespace LicenciasCarpetas.Domain;

public sealed class GlobalSearchResult
{
    public string Module { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Rut { get; set; }
    public string? Detail { get; set; }
    public string? Status { get; set; }
    public string Url { get; set; } = string.Empty;
}

