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
        Assert.Contains("Bonjour le monde", resultat.Texte);
        Assert.Contains("Facture 12345", resultat.Texte);
        Assert.Null(resultat.Confiance);
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
        Assert.Equal(string.Empty, resultat.Texte);
        Assert.Null(resultat.Confiance);
    }
}
