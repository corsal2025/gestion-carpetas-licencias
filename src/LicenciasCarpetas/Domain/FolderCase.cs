namespace LicenciasCarpetas.Domain;

public enum FolderSector
{
    /// <summary>Última carpeta before July 2023 — the physical folder is stored in Archivo.</summary>
    Archivo,

    /// <summary>Última carpeta from July 2023 onwards — the physical folder is in Oficina 43.</summary>
    Oficina43
}

public static class FolderSectorCatalog
{
    /// <summary>The office is called "Oficina 43" everywhere in the department — the enum name
    /// without its space is an implementation detail and must not reach the screen.</summary>
    public static string Display(FolderSector? sector) => sector switch
    {
        FolderSector.Archivo => "Archivo",
        FolderSector.Oficina43 => "Oficina 43",
        _ => "—"
    };
}

/// <summary>
/// One row of a monthly agenda sheet: a citizen cited on a given date at a given office, with the
/// state of their physical folder and the final decision of the process.
/// </summary>
public sealed class FolderCase
{
    public long Id { get; set; }

    /// <summary>"FECHA DE LA CITACIÓN" — the appointment date. Identifies the agenda month.</summary>
    public DateOnly? CitationDate { get; set; }

    /// <summary>"FECHA CUANDO SE SUBIO LA CARPETA" — when the folder was uploaded to Conaset.</summary>
    public DateOnly? FolderUploadedDate { get; set; }

    /// <summary>
    /// "FECHA ULTIMA CARPETA" when the cell holds a real date. In the workbook the same cell is
    /// reused to write a comuna name for cambio-de-domicilio cases; that text lands in
    /// <see cref="LastFolderComuna"/> instead, never here.
    /// </summary>
    public DateOnly? LastFolderDate { get; set; }

    /// <summary>Comuna written in the "FECHA ULTIMA CARPETA" cell: the folder lives in another
    /// municipality and has to be requested from it (cambio de domicilio).</summary>
    public string? LastFolderComuna { get; set; }

    /// <summary>Date of the folder before the last one. Typed by hand; the workbook has no column
    /// for it, so an import never touches it. It never decides the sector — that is the última.</summary>
    public DateOnly? PenultimateFolderDate { get; set; }

    /// <summary>F8 case code, free text. Also typed by hand and absent from the workbook.</summary>
    public string? CodigoF8 { get; set; }

    /// <summary>Clases de licencia que el contribuyente viene a obtener, separadas por coma
    /// ("B,C"). Selección múltiple: una misma citación puede cubrir varias clases.</summary>
    public string? LicenceClasses { get; set; }

    public string? FirstName { get; set; }
    public string? LastName { get; set; }

    /// <summary>"NOMBRE COMPLETO" — what every screen and printed document shows.</summary>
    public string? FullName { get; set; }

    /// <summary>Canonical dotted RUT with a valid check digit, or the raw text when it could not be validated.</summary>
    public string? Rut { get; set; }

    public Office Office { get; set; }

    /// <summary>"ATENCIÓN" column as written by the operator — kept verbatim, it is free text.</summary>
    public string? AttentionNote { get; set; }

    /// <summary>Whether the citizen was actually attended, derived from a non-empty ATENCIÓN cell on import.</summary>
    public bool Attended { get; set; }

    public MoralIdoneity? MoralIdoneity { get; set; }

    public FolderState? FolderState { get; set; }

    /// <summary>Original ESTADO DE LA CARPETA text when it matched no catalog value — nothing is discarded on import.</summary>
    public string? FolderStateRaw { get; set; }

    public FinalDecision? FinalDecision { get; set; }

    /// <summary>Original DECISIÓN FINAL text when it matched no catalog value.</summary>
    public string? FinalDecisionRaw { get; set; }

    /// <summary>Sheet the row came from ("MAYO AV. ARGENTINA"), so any imported row can be traced back.</summary>
    public string? SourceSheet { get; set; }

    /// <summary>1-based row number inside <see cref="SourceSheet"/>.</summary>
    public int SourceRow { get; set; }

    /// <summary>Set on import when the row is incomplete or unreadable: invalid RUT, no name, or a
    /// state/decision outside the catalog. Drives the "Requiere revisión" filter.</summary>
    public bool NeedsReview { get; set; }

    /// <summary>Operator-only bookkeeping checkbox — no effect on any other rule.</summary>
    public bool Marked { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Usuario que editó el caso por última vez desde el dashboard. Null si nadie lo ha
    /// tocado: una importación del libro no cuenta como autoría de nadie.</summary>
    public string? UpdatedBy { get; set; }

    /// <summary>When the operator sent the case to the bin. Null for a live case. A case in the bin
    /// is invisible everywhere except /Papelera, and a re-import updates it without reviving it.</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>When this case was last included in a printed sector list. Once set, it drops off
    /// the next list so the same physical folder is not requested twice; "volver a pedir" clears it.</summary>
    public DateTimeOffset? SectorPrintedAt { get; set; }

    /// <summary>Where the physical folder is filed, derived from the última-carpeta date. Null while
    /// there is no date (including cambio-de-domicilio rows, whose folder is in another comuna).</summary>
    public FolderSector? Sector => LastFolderDate is { } fecha
        ? fecha < new DateOnly(2023, 7, 1) ? FolderSector.Archivo : FolderSector.Oficina43
        : null;

    /// <summary>True when the folder was already uploaded to Conaset, whichever the upload route was.</summary>
    public bool IsUploaded => FolderState is Domain.FolderState.SubidaAConaset
        or Domain.FolderState.SubidaConF8
        or Domain.FolderState.SubidaConOficio
        or Domain.FolderState.CambioDomicilioSubidoAConaset
        or Domain.FolderState.CambioDomicilioSubidoConCorreo;

    /// <summary>Display text for the folder state, catalog value or raw leftover.</summary>
    public string FolderStateText => FolderState is { } state
        ? FolderStateCatalog.Display(state)
        : FolderStateRaw ?? string.Empty;

    public string FinalDecisionText => FinalDecision is { } decision
        ? FinalDecisionCatalog.Display(decision)
        : FinalDecisionRaw ?? string.Empty;
}
