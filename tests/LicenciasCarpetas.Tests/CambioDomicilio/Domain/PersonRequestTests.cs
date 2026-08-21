using LicenciasCarpetas.CambioDomicilio.Domain;

namespace LicenciasCarpetas.Tests.CambioDomicilio.Domain;

public class PersonRequestTests
{
    [Fact]
    public void Sector_IsNull_WhenNoFechaUltimaCarpeta()
    {
        var request = new PersonRequest
        {
            SourceMessageId = "m1",
            SourceSubject = "s",
            SourceSender = "sender@example.com"
        };

        Assert.Null(request.Sector);
    }

    [Fact]
    public void Sector_IsArchivo_BeforeJuly2023()
    {
        var request = new PersonRequest
        {
            SourceMessageId = "m1",
            SourceSubject = "s",
            SourceSender = "sender@example.com",
            FechaUltimaCarpeta = new DateOnly(2023, 6, 30)
        };

        Assert.Equal(FolderSector.Archivo, request.Sector);
    }

    [Fact]
    public void Sector_IsOficina43_FromJuly2023Onwards()
    {
        var request = new PersonRequest
        {
            SourceMessageId = "m1",
            SourceSubject = "s",
            SourceSender = "sender@example.com",
            FechaUltimaCarpeta = new DateOnly(2023, 7, 1)
        };

        Assert.Equal(FolderSector.Oficina43, request.Sector);
    }

    [Fact]
    public void Status_DefaultsToPending()
    {
        var request = new PersonRequest
        {
            SourceMessageId = "m1",
            SourceSubject = "s",
            SourceSender = "sender@example.com"
        };

        Assert.Equal(RequestStatus.Pending, request.Status);
    }

    [Fact]
    public void Destination_DefaultsToNone()
    {
        var request = new PersonRequest
        {
            SourceMessageId = "m1",
            SourceSubject = "s",
            SourceSender = "sender@example.com"
        };

        Assert.Equal(CaseDestination.None, request.Destination);
    }
}
