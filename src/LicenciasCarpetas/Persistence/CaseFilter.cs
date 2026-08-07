using LicenciasCarpetas.Domain;

namespace LicenciasCarpetas.Persistence;

/// <summary>Every filter the cases screen can apply, in one object so paging and counting agree.</summary>
public sealed record CaseFilter
{
    public Office? Office { get; init; }

    /// <summary>Citation month (1-12), paired with <see cref="Year"/> when both are set.</summary>
    public int? Month { get; init; }

    public int? Year { get; init; }

    public FolderState? FolderState { get; init; }

    public FinalDecision? FinalDecision { get; init; }

    public FolderSector? Sector { get; init; }

    public bool OnlyNeedsReview { get; init; }

    /// <summary>Only rows whose última carpeta cell holds a comuna — the folder lives elsewhere.</summary>
    public bool OnlyOtherComuna { get; init; }

    /// <summary>Free text matched against full name and RUT.</summary>
    public string? Search { get; init; }
}
