using LicenciasCarpetas.Configuration;
using LicenciasCarpetas.Domain;
using LicenciasCarpetas.Persistence;
using LicenciasCarpetas.Reporting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LicenciasCarpetas.Dashboard.Pages;

[Authorize]
public class IndexModel(IFolderCaseRepository cases, IExcelCaseExporter exporter, CarpetasOptions options) : PageModel
{
    public IReadOnlyList<FolderCase> Cases { get; private set; } = [];
    public int TotalCount { get; private set; }
    public int NeedsReviewCount { get; private set; }
    public int PageNumber { get; private set; } = 1;
    public int TotalPages { get; private set; } = 1;
    public IReadOnlyList<int> Years { get; private set; } = [];

    /// <summary>RUTs repeated inside the current filter, highlighted like the workbook's COUNTIF.</summary>
    public HashSet<string> DuplicateRuts { get; private set; } = [];

    [BindProperty(SupportsGet = true)]
    public Office? Office { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? Year { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? Month { get; set; }

    [BindProperty(SupportsGet = true)]
    public FolderState? Estado { get; set; }

    [BindProperty(SupportsGet = true)]
    public FinalDecision? Decision { get; set; }

    [BindProperty(SupportsGet = true)]
    public FolderSector? Sector { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool NeedsReview { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool OtherComuna { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true, Name = "p")]
    public int RequestedPage { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public CaseSort Sort { get; set; } = CaseSort.CitationDate;

    [BindProperty(SupportsGet = true, Name = "desc")]
    public bool Descending { get; set; }

    public string? Message { get; set; }
    public bool MessageIsError { get; set; }

    public void OnGet()
    {
        Load();
    }

    public IActionResult OnGetExport()
    {
        var filter = BuildFilter();
        // Everything the filter matches, not just the page on screen and with no row cap: a year of
        // one office is over 15.000 rows, and a silently truncated report is a wrong report.
        var exportable = cases.QueryAll(filter);
        var bytes = exporter.Export(exportable, "Casos");
        var fileName = $"casos-{DateTime.Now:yyyyMMdd-HHmm}.xlsx";
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    public IActionResult OnPostSave(long id, string? nombre, string? rut, string? citacion, string? subida,
        string? ultimaCarpeta, FolderState? estado, FinalDecision? decision, MoralIdoneity? idoneidad, string? atencion,
        string? penultima = null, string? codigoF8 = null)
    {
        var existing = cases.FindById(id);
        if (existing is null)
        {
            return RedirectWithMessage("El caso ya no existe.", isError: true);
        }

        var citationDate = ParseDate(citacion);
        var uploadedDate = ParseDate(subida);

        // Same rule as the workbook: a date means the folder is here, free text means it is in
        // another comuna and has to be requested from it.
        var lastFolderDate = ParseDate(ultimaCarpeta);
        var lastFolderComuna = lastFolderDate is null && !string.IsNullOrWhiteSpace(ultimaCarpeta)
            ? ultimaCarpeta.Trim()
            : null;

        var normalizedRut = RutValidator.NormalizeAndValidate(rut);
        var fullName = string.IsNullOrWhiteSpace(nombre) ? null : nombre.Trim();
        var needsReview = fullName is null || normalizedRut is null || citationDate is null;

        cases.UpdateEditableFields(id, fullName, normalizedRut ?? rut?.Trim(), citationDate, uploadedDate,
            lastFolderDate, lastFolderComuna, estado, decision, idoneidad,
            string.IsNullOrWhiteSpace(atencion) ? null : atencion.Trim(), needsReview);

        // Los dos campos que el Excel no trae van por separado, para que una reimportación no los pise.
        cases.UpdateCaseDetails(id, codigoF8, ParseDate(penultima));

        var message = normalizedRut is null && !string.IsNullOrWhiteSpace(rut)
            ? $"Guardado, pero el RUT '{rut}' no tiene dígito verificador válido — el caso queda en revisión."
            : "Caso guardado.";
        return RedirectWithMessage(message, isError: normalizedRut is null && !string.IsNullOrWhiteSpace(rut));
    }

    /// <summary>Adds a citation row by hand, the way a new line is typed into the agenda sheet.</summary>
    public IActionResult OnPostAdd(string? nombre, string? rut, string? citacion, Office office,
        string? ultimaCarpeta, FolderState? estado, FinalDecision? decision, MoralIdoneity? idoneidad, string? atencion)
    {
        var fullName = string.IsNullOrWhiteSpace(nombre) ? null : nombre.Trim();
        if (fullName is null)
        {
            return RedirectWithMessage("Falta el nombre completo — no se agregó el caso.", isError: true);
        }

        var citationDate = ParseDate(citacion);
        var lastFolderDate = ParseDate(ultimaCarpeta);
        var normalizedRut = RutValidator.NormalizeAndValidate(rut);
        var attention = string.IsNullOrWhiteSpace(atencion) ? null : atencion.Trim();

        var folderCase = new FolderCase
        {
            FullName = fullName,
            Rut = normalizedRut ?? (string.IsNullOrWhiteSpace(rut) ? null : rut.Trim()),
            CitationDate = citationDate,
            Office = office,
            LastFolderDate = lastFolderDate,
            LastFolderComuna = lastFolderDate is null && !string.IsNullOrWhiteSpace(ultimaCarpeta)
                ? ultimaCarpeta.Trim()
                : null,
            FolderState = estado,
            FinalDecision = decision,
            MoralIdoneity = idoneidad,
            AttentionNote = attention,
            Attended = attention is not null,
            NeedsReview = normalizedRut is null || citationDate is null
        };

        cases.Insert(folderCase);

        var warning = normalizedRut is null && !string.IsNullOrWhiteSpace(rut)
            ? $" El RUT '{rut}' no tiene dígito verificador válido, el caso queda en revisión."
            : string.Empty;
        return RedirectWithMessage($"Caso agregado.{warning}", isError: warning.Length > 0);
    }

    public IActionResult OnPostToggleMarked(long id, bool markedValue)
    {
        cases.SetMarked(id, markedValue);
        return new EmptyResult();
    }

    public IActionResult OnPostDelete(long id)
    {
        cases.Delete(id);
        return RedirectWithMessage("Caso movido a la Papelera. Se puede restaurar desde ahí.");
    }

    private void Load()
    {
        var filter = BuildFilter();
        var pageSize = Math.Max(options.PageSize, 10);

        TotalCount = cases.Count(filter);
        TotalPages = Math.Max(1, (int)Math.Ceiling(TotalCount / (double)pageSize));
        PageNumber = Math.Clamp(RequestedPage <= 0 ? 1 : RequestedPage, 1, TotalPages);
        Cases = cases.Query(filter, (PageNumber - 1) * pageSize, pageSize);
        NeedsReviewCount = cases.CountNeedingReview();
        Years = cases.DistinctYears();
        DuplicateRuts = [.. cases.DuplicateRuts(filter)];

        if (TempData["Message"] is string message)
        {
            Message = message;
            MessageIsError = TempData["MessageIsError"] is true;
        }
    }

    private CaseFilter BuildFilter() => new()
    {
        Office = Office,
        Year = Year,
        Month = Month,
        FolderState = Estado,
        FinalDecision = Decision,
        Sector = Sector,
        OnlyNeedsReview = NeedsReview,
        OnlyOtherComuna = OtherComuna,
        Search = Search,
        Sort = Sort,
        Descending = Descending
    };

    /// <summary>Route values for a sortable column header: clicking the active column flips the
    /// direction, clicking another one starts it in its own natural order. Paging resets to 1,
    /// since page 7 of the old ordering means nothing in the new one.</summary>
    public Dictionary<string, string> SortRouteValues(CaseSort column)
    {
        var values = RouteValues(1);
        values["sort"] = column.ToString();
        values["desc"] = (Sort == column && !Descending).ToString();
        return values;
    }

    /// <summary>Arrow shown next to the active column header.</summary>
    public string SortIndicator(CaseSort column)
        => Sort != column ? string.Empty : Descending ? " ▼" : " ▲";

    private IActionResult RedirectWithMessage(string message, bool isError = false)
    {
        TempData["Message"] = message;
        TempData["MessageIsError"] = isError;
        return RedirectToPage(new
        {
            office = Office,
            year = Year,
            month = Month,
            estado = Estado,
            decision = Decision,
            sector = Sector,
            needsReview = NeedsReview,
            otherComuna = OtherComuna,
            search = Search,
            p = RequestedPage
        });
    }

    /// <summary>Current filters as route values, so paging links keep the view the operator set up.</summary>
    public Dictionary<string, string> RouteValues(int page)
    {
        var values = new Dictionary<string, string>
        {
            ["needsReview"] = NeedsReview.ToString(),
            ["otherComuna"] = OtherComuna.ToString(),
            ["p"] = page.ToString()
        };

        if (Office is { } office) values["office"] = office.ToString();
        if (Year is { } year) values["year"] = year.ToString();
        if (Month is { } month) values["month"] = month.ToString();
        if (Estado is { } estado) values["estado"] = estado.ToString();
        if (Decision is { } decision) values["decision"] = decision.ToString();
        if (Sector is { } sector) values["sector"] = sector.ToString();
        if (!string.IsNullOrWhiteSpace(Search)) values["search"] = Search;

        values["sort"] = Sort.ToString();
        values["desc"] = Descending.ToString();

        return values;
    }

    /// <summary>Accepts what the operator is used to typing: "15-03-2024", "15/03/2024", "15 marzo 2024".</summary>
    internal static DateOnly? ParseDate(string? text)
        => SpanishDate.TryParse(text, out var date) ? date : null;

    public static string FormatDate(DateOnly? date) => date is { } value ? value.ToString("dd-MM-yyyy") : string.Empty;
}
