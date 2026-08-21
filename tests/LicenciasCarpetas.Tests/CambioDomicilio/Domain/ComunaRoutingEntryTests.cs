using LicenciasCarpetas.CambioDomicilio.Domain;

namespace LicenciasCarpetas.Tests.CambioDomicilio.Domain;

public class ComunaRoutingEntryTests
{
    [Fact]
    public void ComunaRoutingEntry_ExposesComunaContactEmailAndDomain()
    {
        var entry = new ComunaRoutingEntry("Viña del Mar", "contacto@vina.cl", "vina.cl");

        Assert.Equal("Viña del Mar", entry.Comuna);
        Assert.Equal("contacto@vina.cl", entry.ContactEmail);
        Assert.Equal("vina.cl", entry.Domain);
    }

    [Fact]
    public void ComunaRoutingEntry_EqualityIsByValue()
    {
        var a = new ComunaRoutingEntry("Viña del Mar", "contacto@vina.cl", "vina.cl");
        var b = new ComunaRoutingEntry("Viña del Mar", "contacto@vina.cl", "vina.cl");

        Assert.Equal(a, b);
    }

    [Fact]
    public void IncomingEmail_ExposesAllFields()
    {
        var receivedAt = new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);

        var email = new IncomingEmail(
            "msg-1",
            "conv-1",
            "Cambio de domicilio",
            "sender@vina.cl",
            "cuerpo del correo",
            receivedAt);

        Assert.Equal("msg-1", email.MessageId);
        Assert.Equal("conv-1", email.ConversationId);
        Assert.Equal("Cambio de domicilio", email.Subject);
        Assert.Equal("sender@vina.cl", email.SenderAddress);
        Assert.Equal("cuerpo del correo", email.BodyText);
        Assert.Equal(receivedAt, email.ReceivedAt);
    }
}
