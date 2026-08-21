using LicenciasCarpetas.CambioDomicilio.Data;
using LicenciasCarpetas.CambioDomicilio.Domain;

namespace LicenciasCarpetas.Tests.CambioDomicilio.Routing;

public sealed class FakeDiscardedEmailRepository : IDiscardedEmailRepository
{
    private readonly List<DiscardedEmail> _emails = [];
    private long _nextId = 1;

    public void EnsureSchema() { }

    public bool ExistsBySourceMessageId(string sourceMessageId) =>
        _emails.Any(e => e.SourceMessageId == sourceMessageId);

    public void Insert(DiscardedEmail email)
    {
        email.Id = _nextId++;
        _emails.Add(email);
    }

    public void DeleteBySourceMessageId(string sourceMessageId) =>
        _emails.RemoveAll(e => e.SourceMessageId == sourceMessageId);

    public void Delete(long id) => _emails.RemoveAll(e => e.Id == id);

    public IReadOnlyList<DiscardedEmail> GetAll() => _emails;
}
