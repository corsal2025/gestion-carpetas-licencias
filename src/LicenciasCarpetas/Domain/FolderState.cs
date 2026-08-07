namespace LicenciasCarpetas.Domain;

/// <summary>
/// "ESTADO DE LA CARPETA" — the workbook's own dropdown catalog (see the PLANTILLA MODELO sheets),
/// plus the values that were typed straight into cells over the year.
/// </summary>
public enum FolderState
{
    PrimeraLicencia,
    SubidaAConaset,
    SubidaConF8,
    SubidaConOficio,
    CambioDomicilioSubidoAConaset,
    CambioDomicilioSubidoConCorreo,
    CambioDomicilioSolicitado,
    CambioDomicilio,
    CanjeLicenciaExtranjera,
    SeEncuentraEnArchivos,
    SeEncuentraEnOficina43,
    NoExisteCarpeta,
    CrearOficio,
    CrearCertificado
}

public static class FolderStateCatalog
{
    private static readonly Dictionary<FolderState, string> Displays = new()
    {
        [FolderState.PrimeraLicencia] = "1° LICENCIA",
        [FolderState.SubidaAConaset] = "SUBIDA A CONASET",
        [FolderState.SubidaConF8] = "SUBIDA CON F8",
        [FolderState.SubidaConOficio] = "SUBIDA CON OFICIO",
        [FolderState.CambioDomicilioSubidoAConaset] = "CAMBIO DOM. SUBIDO A CONASET",
        [FolderState.CambioDomicilioSubidoConCorreo] = "CAMBIO DOM. SUBIDO CON CORREO",
        [FolderState.CambioDomicilioSolicitado] = "CAMBIO DE DOMICILIO SOLICITADO",
        [FolderState.CambioDomicilio] = "CAMBIO DE DOMICILIO",
        [FolderState.CanjeLicenciaExtranjera] = "CANJE LIC. EXTRANJERA",
        [FolderState.SeEncuentraEnArchivos] = "SE ENCUENTRA EN ARCHIVOS",
        [FolderState.SeEncuentraEnOficina43] = "SE ENCUENTRA EN OF. 43",
        [FolderState.NoExisteCarpeta] = "NO EXISTE CARPETA",
        [FolderState.CrearOficio] = "CREAR OFICIO",
        [FolderState.CrearCertificado] = "CREAR CERTIFICADO"
    };

    /// <summary>Loose-normalized spellings found in the 2026 workbook, mapped to their canonical value.</summary>
    private static readonly Dictionary<string, FolderState> Aliases = BuildAliases();

    public static IReadOnlyList<FolderState> All { get; } = [.. Displays.Keys];

    public static string Display(FolderState state) => Displays[state];

    public static FolderState? TryResolve(string? text)
    {
        var key = TextNormalizer.NormalizeLoose(text);
        return key.Length == 0 ? null : Aliases.TryGetValue(key, out var state) ? state : null;
    }

    private static Dictionary<string, FolderState> BuildAliases()
    {
        var aliases = new Dictionary<string, FolderState>();

        foreach (var (state, display) in Displays)
        {
            aliases[TextNormalizer.NormalizeLoose(display)] = state;
            aliases[TextNormalizer.NormalizeLoose(state.ToString())] = state;
        }

        void Add(string spelling, FolderState state) => aliases[TextNormalizer.NormalizeLoose(spelling)] = state;

        Add("1 LICENCIA", FolderState.PrimeraLicencia);
        Add("PRIMERA LICENCIA", FolderState.PrimeraLicencia);
        Add("CAMBIO DE DOM SUBIDO CON CORREO", FolderState.CambioDomicilioSubidoConCorreo);
        Add("CAMBIO DOM SUBIDO CON CORREO", FolderState.CambioDomicilioSubidoConCorreo);
        Add("CAMBIO DE DOMICILIO SUBIDO CON CORREO", FolderState.CambioDomicilioSubidoConCorreo);
        Add("CAMBIO DE DOM SUBIDO A CONASET", FolderState.CambioDomicilioSubidoAConaset);
        Add("CAMBIO DE DOMICILIO SUBIDO A CONASET", FolderState.CambioDomicilioSubidoAConaset);
        Add("CANJE", FolderState.CanjeLicenciaExtranjera);
        Add("CANJE LICENCIA EXTRANJERA", FolderState.CanjeLicenciaExtranjera);
        Add("SE ENCUENTRA EN OF.43", FolderState.SeEncuentraEnOficina43);
        Add("SE ENCUENTRA EN OFICINA 43", FolderState.SeEncuentraEnOficina43);
        Add("SE ENCUENTRA EN ARCHIVO", FolderState.SeEncuentraEnArchivos);

        return aliases;
    }
}
