using LicenciasCarpetas.Domain;
using LicenciasCarpetas.F8.Domain;
using LicenciasCarpetas.Persistence;
using Xunit;

namespace LicenciasCarpetas.Tests;

public class GlobalSearchTests
{
    [Fact]
    public void Finds_citizens_across_cases_and_f8_modules()
    {
        using var db = new SqliteTestDatabase();
        db.Cases.Insert(new FolderCase
        {
            FullName = "CARLOS SANTANA GOMEZ",
            Rut = "12.345.678-9",
            CitationDate = new DateOnly(2026, 3, 15),
            Office = Office.AvenidaArgentina,
            FolderState = FolderState.SubidaAConaset
        });

        var f8Repo = new LicenciasCarpetas.F8.Data.UrgentRequestRepository(db.ConnectionString);
        f8Repo.EnsureSchema();
        f8Repo.Insert(new UrgentRequest
        {
            NombreCompleto = "CARLOS SANTANA GOMEZ",
            Rut = "123456789",
            FechaPeticion = new DateOnly(2026, 3, 10),
            Estado = "En proceso",
            Origin = "Manual"
        });

        var searchService = new GlobalSearchService(db.ConnectionString);

        // Search by RUT without dots
        var results = searchService.Search("123456789");
        Assert.True(results.Count >= 2);
        Assert.Contains(results, r => r.Module == "Gestión de Licencias" && r.Title == "CARLOS SANTANA GOMEZ");
        Assert.Contains(results, r => r.Module == "F8 Urgentes" && r.Title == "CARLOS SANTANA GOMEZ");

        // Search by partial name
        var nameResults = searchService.Search("Santana");
        Assert.True(nameResults.Count >= 2);
    }

    [Fact]
    public void Returns_empty_list_for_empty_query()
    {
        using var db = new SqliteTestDatabase();
        var searchService = new GlobalSearchService(db.ConnectionString);

        var results = searchService.Search("   ");
        Assert.Empty(results);
    }
}
