namespace LicenciasCarpetas.Domain;

/// <summary>
/// The two numbers of the "ESCANEADAS Y SUBIDAS" sheet that cannot be derived from the agenda rows:
/// how many folders were scanned and how many were uploaded on a given day. Everything else on the
/// statistics screen (agendadas, atendidas, % de atención) is computed from the cases themselves.
/// </summary>
public sealed class DailyCounter
{
    public DateOnly Date { get; set; }
    public int? Scanned { get; set; }
    public int? Uploaded { get; set; }
}
