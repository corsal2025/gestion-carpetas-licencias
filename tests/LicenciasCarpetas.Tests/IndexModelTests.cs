using LicenciasCarpetas.Configuration;
using LicenciasCarpetas.Dashboard.Pages;
using LicenciasCarpetas.Domain;
using LicenciasCarpetas.Persistence;
using LicenciasCarpetas.Reporting;
using Microsoft.AspNetCore.Mvc;

namespace LicenciasCarpetas.Tests;

public class IndexModelTests
{
    /// <summary>Captures what the page asked to export, instead of building a real workbook.</summary>
    private sealed class SpyExporter : IExcelCaseExporter
    {
        public IReadOnlyList<FolderCase> Exported { get; private set; } = [];

        public byte[] Export(IReadOnlyList<FolderCase> cases, string sheetTitle)
        {
            Exported = cases;
            return [1, 2, 3];
        }
    }

    /// <summary>Inserts in one transaction: these tests need thousands of rows and the repository's
    /// row-by-row insert would make the suite crawl.</summary>
    private static void Seed(SqliteTestDatabase db, int count, Office office = Office.AvenidaArgentina)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(db.ConnectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO FolderCase (FullName, CitationDate, Office, Attended, SourceSheet, SourceRow,
                                    NeedsReview, Marked, CreatedAt, UpdatedAt)
            VALUES ($name, '2026-01-02', $office, 0, 'ENERO AV. ARGENTINA', $row, 0, 0, $now, $now)
            """;
        var name = command.Parameters.Add("$name", Microsoft.Data.Sqlite.SqliteType.Text);
        var row = command.Parameters.Add("$row", Microsoft.Data.Sqlite.SqliteType.Integer);
        command.Parameters.AddWithValue("$office", (int)office);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));

        for (var i = 0; i < count; i++)
        {
            name.Value = $"PERSONA {(int)office}-{i:D5}";
            row.Value = i + 3;
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>
    /// The filtered view is the whole point of the export: a year of one office is over 15.000 rows,
    /// and silently handing back a truncated file is worse than refusing to build one.
    /// </summary>
    [Fact]
    public void The_export_covers_every_filtered_case_however_many_there_are()
    {
        using var db = new SqliteTestDatabase();
        Seed(db, 6000);
        var exporter = new SpyExporter();
        var model = new IndexModel(db.Cases, exporter, new CarpetasOptions { PageSize = 100 });

        var result = model.OnGetExport();

        Assert.IsType<FileContentResult>(result);
        Assert.Equal(6000, exporter.Exported.Count);
    }

    [Theory]
    [InlineData(50, 50)]
    [InlineData(100, 100)]
    [InlineData(200, 200)]
    public void The_operator_chooses_how_many_rows_to_see(int chosen, int expected)
    {
        using var db = new SqliteTestDatabase();
        Seed(db, 300);
        var model = new IndexModel(db.Cases, new SpyExporter(), new CarpetasOptions { PageSize = 100 })
        {
            PageSize = chosen
        };

        model.OnGet();

        Assert.Equal(expected, model.Cases.Count);
    }

    /// <summary>A hand-edited URL cannot ask for 20.000 rows in one page.</summary>
    [Theory]
    [InlineData(9999)]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(37)]
    public void An_unsupported_page_size_falls_back_to_the_configured_one(int chosen)
    {
        using var db = new SqliteTestDatabase();
        Seed(db, 150);
        var model = new IndexModel(db.Cases, new SpyExporter(), new CarpetasOptions { PageSize = 100 })
        {
            PageSize = chosen
        };

        model.OnGet();

        Assert.Equal(100, model.Cases.Count);
        Assert.Equal(100, model.PageSize);
    }

    [Fact]
    public void The_chosen_size_survives_paging_and_filtering()
    {
        using var db = new SqliteTestDatabase();
        var model = new IndexModel(db.Cases, new SpyExporter(), new CarpetasOptions()) { PageSize = 50 };

        Assert.Equal("50", model.RouteValues(2)["size"]);
    }

    [Fact]
    public void The_export_respects_the_active_filters()
    {
        using var db = new SqliteTestDatabase();
        Seed(db, 30);
        Seed(db, 12, Office.Placilla);
        var exporter = new SpyExporter();
        var model = new IndexModel(db.Cases, exporter, new CarpetasOptions { PageSize = 100 })
        {
            Office = Office.Placilla
        };

        model.OnGetExport();

        Assert.Equal(12, exporter.Exported.Count);
        Assert.All(exporter.Exported, item => Assert.Equal(Office.Placilla, item.Office));
    }
}
