using DetectDoc.Infrastructure.Engines;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace DetectDoc.Tests;

public class PdfPigNativeTextEngineTests
{
    [Fact]
    public void Extract_ExtraitLeTexteDUnPdfNatif()
    {
        // Arrange
        var builder = new PdfDocumentBuilder();
        var page1 = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
        page1.AddText("Bonjour le monde, ceci est la page 1", 12, new UglyToad.PdfPig.Core.PdfPoint(50, 700), builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica));

        var page2 = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
        page2.AddText("Facture 12345", 12, new UglyToad.PdfPig.Core.PdfPoint(50, 700), builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica));

        byte[] pdfOctets = builder.Build();
        using var stream = new MemoryStream(pdfOctets);

        using var engine = new PdfPigNativeTextEngine();

        // Act
        var resultat = engine.Extract(stream);

        // Assert
        Assert.Collection(
            resultat,
            page =>
            {
                Assert.Equal(1, page.NumeroPage);
                Assert.Contains("Bonjour le monde", page.Texte);
                Assert.Null(page.Confiance);
            },
            page =>
            {
                Assert.Equal(2, page.NumeroPage);
                Assert.Contains("Facture 12345", page.Texte);
                Assert.Null(page.Confiance);
            });
        Assert.Equal("PdfPig", engine.Name);
    }

    [Fact]
    public void Extract_RetourneTexteVidePourUnePageSansTexteNatif()
    {
        // Arrange
        var builder = new PdfDocumentBuilder();
        builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
        byte[] pdfOctets = builder.Build();
        using var stream = new MemoryStream(pdfOctets);

        using var engine = new PdfPigNativeTextEngine();

        // Act
        var resultat = engine.Extract(stream);

        // Assert
        var page = Assert.Single(resultat);
        Assert.Equal(1, page.NumeroPage);
        Assert.Equal(string.Empty, page.Texte);
        Assert.Null(page.Confiance);
    }

    [Fact]
    public void Extract_RefuseUnFluxSansSignaturePdf()
    {
        using var stream = new MemoryStream("pas un PDF"u8.ToArray());
        using var engine = new PdfPigNativeTextEngine();

        var exception = Assert.Throws<InvalidDataException>(() => engine.Extract(stream));

        Assert.Contains("signature PDF", exception.Message);
    }

    [Fact]
    public void Extract_RefusePlusDeVingtPages()
    {
        var builder = new PdfDocumentBuilder();
        for (var numeroPage = 0; numeroPage < 21; numeroPage++)
        {
            builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
        }

        using var stream = new MemoryStream(builder.Build());
        using var engine = new PdfPigNativeTextEngine();

        var exception = Assert.Throws<InvalidDataException>(() => engine.Extract(stream));

        Assert.Contains("entre 1 et 20 pages", exception.Message);
    }

    [Fact]
    public void Extract_RespecteUneAnnulationDejaDemandee()
    {
        var builder = new PdfDocumentBuilder();
        builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
        using var stream = new MemoryStream(builder.Build());
        using var engine = new PdfPigNativeTextEngine();

        Assert.Throws<OperationCanceledException>(
            () => engine.Extract(stream, new CancellationToken(canceled: true)));
    }
}
