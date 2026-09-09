using DetectDoc.Infrastructure.Engines;
using Xunit;

namespace DetectDoc.Tests;

public class PdfPigNativeTextEngineTests
{
    [Fact]
    public void Extract_ExtraitLeTexteDUnPdfNatif()
    {
        using var stream = new MemoryStream(
            PdfDeTest.Construire("Bonjour le monde, ceci est la page 1", "Facture 12345"));
        var engine = new PdfPigNativeTextEngine();

        var resultat = engine.Extract(stream);

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
        using var stream = new MemoryStream(PdfDeTest.Construire(string.Empty));
        var engine = new PdfPigNativeTextEngine();

        var resultat = engine.Extract(stream);

        var page = Assert.Single(resultat);
        Assert.Equal(1, page.NumeroPage);
        Assert.Equal(string.Empty, page.Texte);
        Assert.Null(page.Confiance);
    }

    [Fact]
    public void Extract_RefuseUnFluxSansSignaturePdf()
    {
        using var stream = new MemoryStream("pas un PDF"u8.ToArray());
        var engine = new PdfPigNativeTextEngine();

        var exception = Assert.Throws<InvalidDataException>(() => engine.Extract(stream));

        Assert.Contains("signature PDF", exception.Message);
    }

    [Fact]
    public void Extract_RespecteUneAnnulationDejaDemandee()
    {
        using var stream = new MemoryStream(PdfDeTest.Construire(string.Empty));
        var engine = new PdfPigNativeTextEngine();

        Assert.Throws<OperationCanceledException>(
            () => engine.Extract(stream, new CancellationToken(canceled: true)));
    }

    [Fact]
    public void PpOcr_RefuseZeroReconnaissanceParallele()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PpOcrEngine("tiny", "modeles", reconnaissancesParalleles: 0));
    }

    [Fact]
    [Trait("Category", "Corpus")]
    public void Extract_ExtraitLeTexteDUnDevisNatifDuCorpus()
    {
        var cheminPdf = Path.Combine(ObtenirRacineProjet(), "corpus", "raw", "dev-0003.pdf");

        using var stream = File.OpenRead(cheminPdf);
        var engine = new PdfPigNativeTextEngine();

        var resultat = engine.Extract(stream);

        var page = Assert.Single(resultat);
        Assert.Equal(1, page.NumeroPage);
        Assert.False(string.IsNullOrWhiteSpace(page.Texte));
        Assert.Contains("DEVIS", page.Texte);
        Assert.Null(page.Confiance);
    }

    private static string ObtenirRacineProjet()
    {
        for (var dossier = new DirectoryInfo(AppContext.BaseDirectory); dossier is not null; dossier = dossier.Parent)
        {
            if (File.Exists(Path.Combine(dossier.FullName, "DetectDoc.slnx")))
            {
                return dossier.FullName;
            }
        }

        throw new DirectoryNotFoundException("La racine du projet Detect-Doc est introuvable.");
    }
}
