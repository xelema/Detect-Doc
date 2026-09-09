using DetectDoc.Core.Engines;
using Xunit;

namespace DetectDoc.Tests;

public class ExtractionAutomatiqueTests
{
    [Fact]
    public void Extract_ConservePdfPigQuandToutesLesPagesOntDuTexte()
    {
        var natif = new ExtracteurFictif("pdfpig", [new(1, "Texte natif", null)]);
        var ocr = new ExtracteurFictif("ppocr-tiny", [new(1, "Texte OCR", 0.9f)]);

        var resultat = new ExtractionAutomatique(natif, () => ocr).Extract(new MemoryStream());

        Assert.Equal("pdfpig", resultat.Moteur);
        Assert.Equal(0, ocr.Appels);
    }

    [Fact]
    public void Extract_BasculeVersOcrQuandUnePageEstVide()
    {
        var natif = new ExtracteurFictif("pdfpig", [new(1, "Texte natif", null), new(2, "", null)]);
        var ocr = new ExtracteurFictif("ppocr-tiny", [new(1, "Texte OCR", 0.9f), new(2, "Suite OCR", 0.9f)]);

        var resultat = new ExtractionAutomatique(natif, () => ocr).Extract(new MemoryStream());

        Assert.Equal("ppocr-tiny", resultat.Moteur);
        Assert.Equal(1, ocr.Appels);
        Assert.Equal("Suite OCR", resultat.Pages[1].Texte);
    }

    [Fact]
    public void Extract_NeConstruitPasLOcrQuandLeTexteNatifSuffit()
    {
        var natif = new ExtracteurFictif("pdfpig", [new(1, "Texte natif", null)]);

        var resultat = new ExtractionAutomatique(
            natif,
            () => throw new InvalidOperationException("Le modèle PP-OCR requis est introuvable."))
            .Extract(new MemoryStream());

        Assert.Equal("pdfpig", resultat.Moteur);
    }

    private sealed class ExtracteurFictif(string nom, IReadOnlyList<ResultatExtractionPage> pages) : ITextExtractionEngine
    {
        public int Appels { get; private set; }
        public string Name => nom;

        public IReadOnlyList<ResultatExtractionPage> Extract(Stream fluxDocument, CancellationToken cancellationToken = default)
        {
            Appels++;
            return pages;
        }
    }
}
