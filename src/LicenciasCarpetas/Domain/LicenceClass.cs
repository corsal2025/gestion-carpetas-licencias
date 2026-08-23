namespace LicenciasCarpetas.Domain;

/// <summary>
/// Clases de licencia de conducir chilenas. A1–A5 son profesionales; B y C, no profesionales;
/// D, E y F, especiales (maquinaria, tracción animal y vehículos policiales o militares).
/// </summary>
public enum LicenceClass
{
    A1,
    A2,
    A3,
    A4,
    A5,
    B,
    C,
    D,
    E,
    F
}

public static class LicenceClassCatalog
{
    /// <summary>En orden de catálogo, que es el orden en que se muestran y se guardan.</summary>
    public static IReadOnlyList<LicenceClass> All { get; } = [.. Enum.GetValues<LicenceClass>()];

    public static string Display(LicenceClass licence) => licence.ToString();

    public static bool IsProfessional(LicenceClass licence)
        => licence is LicenceClass.A1 or LicenceClass.A2 or LicenceClass.A3
            or LicenceClass.A4 or LicenceClass.A5;

    /// <summary>
    /// Lee la selección guardada ("B,C"). Un código desconocido se ignora en vez de reventar: la
    /// columna es texto y puede traer restos de una versión anterior o de una edición a mano.
    /// </summary>
    public static IReadOnlyList<LicenceClass> Parse(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return [];
        }

        var selected = new List<LicenceClass>();
        foreach (var piece in stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse<LicenceClass>(piece, ignoreCase: true, out var licence) && !selected.Contains(licence))
            {
                selected.Add(licence);
            }
        }

        selected.Sort();
        return selected;
    }

    /// <summary>Guarda en orden de catálogo y sin repetidos, para que dos selecciones iguales se
    /// escriban igual y las estadísticas puedan agrupar sin sorpresas.</summary>
    public static string? Serialize(IEnumerable<LicenceClass> selection)
    {
        var ordered = selection.Distinct().OrderBy(licence => licence).ToList();
        return ordered.Count == 0 ? null : string.Join(',', ordered.Select(Display));
    }

    /// <summary>Etiqueta corta para la tabla: "B, C".</summary>
    public static string DisplayList(string? stored)
    {
        var selection = Parse(stored);
        return selection.Count == 0 ? string.Empty : string.Join(", ", selection.Select(Display));
    }
}
