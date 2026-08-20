using LicenciasCarpetas.Domain;

namespace LicenciasCarpetas.Import;

/// <summary>Turns a raw workbook row into a <see cref="FolderCase"/>, flagging whatever it could not
/// read instead of dropping it.</summary>
public static class AgendaRowMapper
{
    /// <summary>Returns null when the row carries no person at all (spacer rows, trailing blanks).</summary>
    public static FolderCase? Map(RawAgendaRow raw, AgendaSheet sheet, int rowNumber)
    {
        var fullName = CellValue.ToText(raw.FullName);
        var firstName = CellValue.ToText(raw.FirstName);
        var lastName = CellValue.ToText(raw.LastName);
        var rawRut = CellValue.ToText(raw.Rut);

        if (fullName is null && firstName is null && lastName is null && rawRut is null)
        {
            return null;
        }

        // "NOMBRE COMPLETO" is a formula in the workbook and sometimes ends up empty even though the
        // two source columns are filled — rebuild it rather than leaving the row nameless.
        fullName ??= string.Join(' ', new[] { firstName, lastName }.Where(part => part is not null)).Trim();
        if (fullName.Length == 0)
        {
            fullName = null;
        }
        fullName = TextNormalizer.DisplayUpper(fullName);

        var validatedRut = RutValidator.NormalizeAndValidate(rawRut);
        var rawState = CellValue.ToText(raw.FolderState);
        var state = FolderStateCatalog.TryResolve(rawState);
        var rawDecision = CellValue.ToText(raw.FinalDecision);
        var decision = FinalDecisionCatalog.TryResolve(rawDecision);
        var attention = CellValue.ToText(raw.Attention);

        // Same cell, two meanings: a date means the folder is here in Valparaíso, a comuna name means
        // it has to be requested from that municipality (cambio de domicilio).
        var lastFolderDate = CellValue.ToDate(raw.LastFolder);
        var lastFolderComuna = lastFolderDate is null ? CellValue.ToText(raw.LastFolder) : null;

        // "SE ENCUENTRA EN ARCHIVOS" / "EN OF. 43" only repeat what the sector already says, and the
        // sector is derived from the date above. With a date present they are dropped, so importing
        // the workbook again cannot resurrect the labels the operator retired. Without a date they
        // are the only record of where the folder is, and they stay.
        if (lastFolderDate is not null && state is Domain.FolderState.SeEncuentraEnArchivos
            or Domain.FolderState.SeEncuentraEnOficina43)
        {
            state = null;
            rawState = null;
        }

        var folderCase = new FolderCase
        {
            CitationDate = CellValue.ToDate(raw.CitationDate),
            FolderUploadedDate = CellValue.ToDate(raw.FolderUploadedDate),
            LastFolderDate = lastFolderDate,
            LastFolderComuna = lastFolderComuna,
            FirstName = firstName,
            LastName = lastName,
            FullName = fullName,
            Rut = validatedRut ?? rawRut,
            Office = ResolveOffice(attention, sheet),
            AttentionNote = attention,
            Attended = attention is not null,
            MoralIdoneity = MoralIdoneityCatalog.TryResolve(CellValue.ToText(raw.MoralIdoneity)),
            FolderState = state,
            FolderStateRaw = state is null ? rawState : null,
            FinalDecision = decision,
            FinalDecisionRaw = decision is null ? rawDecision : null,
            SourceSheet = sheet.Title,
            SourceRow = rowNumber
        };

        folderCase.NeedsReview = fullName is null
            || validatedRut is null
            || folderCase.CitationDate is null
            || folderCase.FolderStateRaw is not null
            || folderCase.FinalDecisionRaw is not null;

        return folderCase;
    }

    /// <summary>The ATENCIÓN cell ("SI, EN PLACILLA") wins over the sheet when both are readable —
    /// a person cited at one office is occasionally attended at another.</summary>
    private static Office ResolveOffice(string? attention, AgendaSheet sheet)
        => OfficeCatalog.TryResolve(attention) ?? sheet.Office;
}
