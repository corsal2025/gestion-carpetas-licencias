namespace LicenciasCarpetas.F8.Domain;

public enum FolderSector { Archivo, Oficina43 }

/// <summary>
/// Sector is derived from FechaPenultimaCarpeta, not stored: cases whose penultimate-folder
/// date falls before the archive cutover (June 2023) go to the old Archivo, everything from
/// June 2023 on is Oficina43.
/// </summary>
public static class FolderSectorCalculator
{
    private static readonly DateOnly Cutoff = new(2023, 6, 1);

    public static FolderSector? For(DateOnly? fechaPenultimaCarpeta) =>
        fechaPenultimaCarpeta is { } fecha
            ? fecha < Cutoff ? FolderSector.Archivo : FolderSector.Oficina43
            : null;
}
