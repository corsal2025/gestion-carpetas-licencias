namespace LicenciasCarpetas.F8.Domain;

/// <summary>
/// A single dubious-cell finding raised during import (or, in principle, any
/// future re-validation). Carries the column, a machine reason code, and the
/// raw offending value so the Review page can show it verbatim.
/// </summary>
public sealed record ImportFlag(long Id, long RequestId, string ColumnName, string ReasonCode, string? RawValue, DateTimeOffset CreatedAt)
{
    public static class ReasonCodes
    {
        public const string DateOutOfRange = "DATE_OUT_OF_RANGE";
        public const string Unparseable = "UNPARSEABLE";
        public const string RutCheckDigit = "RUT_CHECK_DIGIT";
        public const string RutMissing = "RUT_MISSING";
        public const string UnknownEstado = "UNKNOWN_ESTADO";
        public const string UnknownEstadoActual = "UNKNOWN_ESTADO_ACTUAL";
    }
}
