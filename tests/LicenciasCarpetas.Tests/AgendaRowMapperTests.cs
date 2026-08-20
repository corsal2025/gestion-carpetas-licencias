using LicenciasCarpetas.Domain;
using LicenciasCarpetas.Import;

namespace LicenciasCarpetas.Tests;

public class AgendaRowMapperTests
{
    private static readonly AgendaSheet Sheet = new("ENERO AV. ARGENTINA", Office.AvenidaArgentina, 1);

    private static RawAgendaRow Row(
        object? citation = null,
        object? uploaded = null,
        object? lastFolder = null,
        object? firstName = null,
        object? lastName = null,
        object? fullName = null,
        object? rut = null,
        object? attention = null,
        object? idoneity = null,
        object? state = null,
        object? decision = null)
        => new(citation, uploaded, lastFolder, firstName, lastName, fullName, rut, attention, idoneity, state, decision);

    [Fact]
    public void Maps_a_complete_row()
    {
        var mapped = AgendaRowMapper.Map(
            Row(citation: new DateTime(2026, 1, 2),
                uploaded: new DateTime(2026, 1, 20),
                lastFolder: new DateTime(2024, 9, 1),
                firstName: "Reinaldo",
                lastName: "Pizarro Aravena",
                fullName: "REINALDO PIZARRO ARAVENA",
                rut: "13.025.150-1",
                attention: "SI, EN AV. ARGENTINA",
                state: "SUBIDA A CONASET",
                decision: "OTORGADO"),
            Sheet,
            rowNumber: 3);

        Assert.NotNull(mapped);
        Assert.Equal(new DateOnly(2026, 1, 2), mapped.CitationDate);
        Assert.Equal(new DateOnly(2026, 1, 20), mapped.FolderUploadedDate);
        Assert.Equal(new DateOnly(2024, 9, 1), mapped.LastFolderDate);
        Assert.Null(mapped.LastFolderComuna);
        Assert.Equal("REINALDO PIZARRO ARAVENA", mapped.FullName);
        Assert.Equal("13.025.150-1", mapped.Rut);
        Assert.Equal(Office.AvenidaArgentina, mapped.Office);
        Assert.True(mapped.Attended);
        Assert.Equal(FolderState.SubidaAConaset, mapped.FolderState);
        Assert.Equal(FinalDecision.Otorgado, mapped.FinalDecision);
        Assert.Equal(FolderSector.Oficina43, mapped.Sector);
        Assert.False(mapped.NeedsReview);
        Assert.Equal(3, mapped.SourceRow);
        Assert.Equal("ENERO AV. ARGENTINA", mapped.SourceSheet);
    }

    [Fact]
    public void A_comuna_in_the_last_folder_column_is_not_read_as_a_date()
    {
        var mapped = AgendaRowMapper.Map(
            Row(citation: new DateTime(2026, 1, 2),
                lastFolder: "LIMACHE",
                fullName: "PAMELA MARGARITA ROJAS MOYANO",
                rut: "10.904.318-4",
                state: "CAMBIO DOM. SUBIDO A CONASET"),
            Sheet,
            rowNumber: 9);

        Assert.NotNull(mapped);
        Assert.Null(mapped.LastFolderDate);
        Assert.Equal("LIMACHE", mapped.LastFolderComuna);
        Assert.Null(mapped.Sector);
    }

    [Fact]
    public void Folders_from_before_july_2023_are_filed_in_archivo()
    {
        var mapped = AgendaRowMapper.Map(
            Row(citation: new DateTime(2026, 1, 2),
                lastFolder: new DateTime(2023, 6, 30),
                fullName: "JOSE SEGUNDO CAYUQUEO HUENCHUN",
                rut: "5.667.048-3"),
            Sheet,
            rowNumber: 4);

        Assert.Equal(FolderSector.Archivo, mapped!.Sector);
    }

    [Fact]
    public void An_invalid_rut_is_kept_verbatim_and_flagged_for_review()
    {
        var mapped = AgendaRowMapper.Map(
            Row(citation: new DateTime(2026, 1, 2), fullName: "JUAN PEREZ", rut: "13.025.150-9"),
            Sheet,
            rowNumber: 5);

        Assert.NotNull(mapped);
        Assert.Equal("13.025.150-9", mapped.Rut);
        Assert.True(mapped.NeedsReview);
    }

    [Fact]
    public void An_unknown_state_is_preserved_as_raw_text_and_flagged()
    {
        var mapped = AgendaRowMapper.Map(
            Row(citation: new DateTime(2026, 1, 2),
                fullName: "JUAN PEREZ",
                rut: "13.025.150-1",
                state: "ALGO NUEVO QUE ESCRIBIERON"),
            Sheet,
            rowNumber: 6);

        Assert.NotNull(mapped);
        Assert.Null(mapped.FolderState);
        Assert.Equal("ALGO NUEVO QUE ESCRIBIERON", mapped.FolderStateRaw);
        Assert.True(mapped.NeedsReview);
    }

    [Fact]
    public void The_full_name_is_rebuilt_when_the_formula_column_came_out_empty()
    {
        var mapped = AgendaRowMapper.Map(
            Row(citation: new DateTime(2026, 1, 2), firstName: "Gabriel", lastName: "Cancino", rut: "13.025.150-1"),
            Sheet,
            rowNumber: 7);

        Assert.Equal("GABRIEL CANCINO", mapped!.FullName);
    }

    [Fact]
    public void The_attention_cell_wins_over_the_sheet_office()
    {
        var mapped = AgendaRowMapper.Map(
            Row(citation: new DateTime(2026, 1, 2),
                fullName: "JUAN PEREZ",
                rut: "13.025.150-1",
                attention: "SI, EN PLACILLA"),
            Sheet,
            rowNumber: 8);

        Assert.Equal(Office.Placilla, mapped!.Office);
    }

    /// <summary>
    /// "SE ENCUENTRA EN ARCHIVOS" / "EN OF. 43" say the same thing the sector already says, and the
    /// sector is derived from the última-carpeta date. With a date present the state is dropped as
    /// redundant, so an import cannot bring back the labels the operator retired.
    /// </summary>
    [Theory]
    [InlineData("SE ENCUENTRA EN ARCHIVOS")]
    [InlineData("SE ENCUENTRA EN OF. 43")]
    public void A_sector_state_is_dropped_when_the_date_already_gives_the_sector(string state)
    {
        var mapped = AgendaRowMapper.Map(
            Row(citation: new DateTime(2026, 1, 2),
                lastFolder: new DateTime(2020, 5, 1),
                fullName: "JUAN PEREZ",
                rut: "13.025.150-1",
                state: state),
            Sheet,
            rowNumber: 10);

        Assert.Null(mapped!.FolderState);
        Assert.Null(mapped.FolderStateRaw);
        Assert.Equal(FolderSector.Archivo, mapped.Sector);
        Assert.False(mapped.NeedsReview);
    }

    /// <summary>Without a date the sector cannot be derived, so the label is the only thing that
    /// says where the folder is — and it is kept.</summary>
    [Fact]
    public void A_sector_state_is_kept_when_there_is_no_date_to_derive_it_from()
    {
        var mapped = AgendaRowMapper.Map(
            Row(citation: new DateTime(2026, 1, 2),
                fullName: "JUAN PEREZ",
                rut: "13.025.150-1",
                state: "SE ENCUENTRA EN ARCHIVOS"),
            Sheet,
            rowNumber: 11);

        Assert.Equal(FolderState.SeEncuentraEnArchivos, mapped!.FolderState);
    }

    /// <summary>"CREAR OFICIO" is an action, not a location: nothing else records it, so it stays.</summary>
    [Fact]
    public void A_retired_state_that_is_not_about_the_sector_is_kept()
    {
        var mapped = AgendaRowMapper.Map(
            Row(citation: new DateTime(2026, 1, 2),
                lastFolder: new DateTime(2020, 5, 1),
                fullName: "JUAN PEREZ",
                rut: "13.025.150-1",
                state: "CREAR OFICIO"),
            Sheet,
            rowNumber: 12);

        Assert.Equal(FolderState.CrearOficio, mapped!.FolderState);
    }

    [Fact]
    public void A_row_without_any_person_is_skipped()
        => Assert.Null(AgendaRowMapper.Map(Row(citation: new DateTime(2026, 1, 2)), Sheet, rowNumber: 40));

    [Fact]
    public void An_empty_attention_cell_means_the_person_was_not_attended()
    {
        var mapped = AgendaRowMapper.Map(
            Row(citation: new DateTime(2026, 1, 2), fullName: "JUAN PEREZ", rut: "13.025.150-1"),
            Sheet,
            rowNumber: 9);

        Assert.False(mapped!.Attended);
    }
}
