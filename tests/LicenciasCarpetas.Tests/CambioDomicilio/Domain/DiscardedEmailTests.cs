using LicenciasCarpetas.CambioDomicilio.Domain;

namespace LicenciasCarpetas.Tests.CambioDomicilio.Domain;

public class DiscardedEmailTests
{
    [Fact]
    public void DiscardedAt_DefaultsToUtcNow()
    {
        var before = DateTimeOffset.UtcNow;

        var email = new DiscardedEmail
        {
            SourceMessageId = "m1",
            SourceSubject = "s",
            SourceSender = "sender@example.com",
            Reason = "unknown domain"
        };

        var after = DateTimeOffset.UtcNow;

        Assert.InRange(email.DiscardedAt, before, after);
    }
}
