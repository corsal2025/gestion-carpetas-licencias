using LicenciasCarpetas.CambioDomicilio.Data;
using LicenciasCarpetas.CambioDomicilio.Domain;

namespace LicenciasCarpetas.Tests.CambioDomicilio.Data;

public class DiscardedEmailRepositoryTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"licencias-carpetas-de-{Guid.NewGuid():N}.db");
    private readonly DiscardedEmailRepository _repository;

    public DiscardedEmailRepositoryTests()
    {
        _repository = new DiscardedEmailRepository($"Data Source={_path}");
        _repository.EnsureSchema();
    }

    private static DiscardedEmail Email(string sourceMessageId = "msg-1") => new()
    {
        SourceMessageId = sourceMessageId,
        SourceSubject = "Solicitud sin comuna reconocida",
        SourceSender = "desconocido@ejemplo.cl",
        Reason = "Dominio no está en el directorio de comunas"
    };

    [Fact]
    public void Inserts_and_lists_a_discarded_email()
    {
        _repository.Insert(Email());

        var all = _repository.GetAll();

        Assert.Single(all);
        Assert.Equal("msg-1", all[0].SourceMessageId);
    }

    [Fact]
    public void ExistsBySourceMessageId_reflects_inserted_rows()
    {
        _repository.Insert(Email("msg-known"));

        Assert.True(_repository.ExistsBySourceMessageId("msg-known"));
        Assert.False(_repository.ExistsBySourceMessageId("msg-unknown"));
    }

    [Fact]
    public void DeleteBySourceMessageId_removes_the_row()
    {
        _repository.Insert(Email("msg-to-delete"));

        _repository.DeleteBySourceMessageId("msg-to-delete");

        Assert.False(_repository.ExistsBySourceMessageId("msg-to-delete"));
    }

    [Fact]
    public void Delete_by_id_removes_the_row()
    {
        _repository.Insert(Email());
        var id = _repository.GetAll()[0].Id;

        _repository.Delete(id);

        Assert.Empty(_repository.GetAll());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}
