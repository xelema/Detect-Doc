using DetectDoc.Core.Engines;
using DetectDoc.Infrastructure.Engines;
using Xunit;

namespace DetectDoc.Tests;

public class ExtractionSelectorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("auto")]
    [InlineData("AUTO")]
    public void Extract_AutoAppliqueLaPolitiqueDeRepli(string? engine)
    {
        var natif = new ExtracteurFictif("pdfpig", [new(1, "Texte natif", null)]);
        var ocr = new ExtracteurFictif("ppocr-tiny", [new(1, "Texte OCR", 0.9f)]);
        var selecteur = CreerSelecteur(natif, ocr);

        var resultat = selecteur.Extract(engine, new MemoryStream());

        Assert.Equal("pdfpig", resultat.Moteur);
        Assert.Equal(1, natif.Appels);
        Assert.Equal(0, ocr.Appels);
    }

    [Fact]
    public void Extract_AutoBasculeSurLOcrQuandUnePageEstVide()
    {
        var natif = new ExtracteurFictif("pdfpig", [new(1, "", null)]);
        var ocr = new ExtracteurFictif("ppocr-tiny", [new(1, "Texte OCR", 0.9f)]);
        var selecteur = CreerSelecteur(natif, ocr);

        var resultat = selecteur.Extract("auto", new MemoryStream());

        Assert.Equal("ppocr-tiny", resultat.Moteur);
        Assert.Equal(1, ocr.Appels);
    }

    [Theory]
    [InlineData("pdfpig")]
    [InlineData("PDFPIG")]
    public void Extract_PdfPigUtiliseLeMoteurNommeSansPasserParAuto(string engine)
    {
        var natifDirect = new ExtracteurFictif("pdfpig", [new(1, "Natif direct", null)]);
        var natifAuto = new ExtracteurFictif("pdfpig", [new(1, "", null)]);
        var ocr = new ExtracteurFictif("ppocr-tiny", [new(1, "Texte OCR", 0.9f)]);
        var selecteur = new ExtractionSelector(new AutomaticExtraction(natifAuto, () => ocr), natifDirect, () => ocr);

        var resultat = selecteur.Extract(engine, new MemoryStream());

        Assert.Equal("Natif direct", resultat.Pages[0].Texte);
        Assert.Equal(0, natifAuto.Appels);
        Assert.Equal(0, ocr.Appels);
    }

    [Fact]
    public void Extract_PpOcrInvoqueLaFabriquePuisLeMoteur()
    {
        var ocr = new ExtracteurFictif("ppocr-tiny", [new(1, "Texte OCR", 0.9f)]);
        var selecteur = CreerSelecteur(new ExtracteurFictif("pdfpig", []), ocr);

        var resultat = selecteur.Extract("ppocr-tiny", new MemoryStream());

        Assert.Equal("ppocr-tiny", resultat.Moteur);
        Assert.Equal(1, ocr.Appels);
    }

    [Fact]
    public void Extract_NeConstruitPasLOcrPourUnAutreMoteur()
    {
        var fabriques = 0;
        var natif = new ExtracteurFictif("pdfpig", [new(1, "Texte natif", null)]);
        var selecteur = new ExtractionSelector(
            new AutomaticExtraction(natif, () => throw new InvalidOperationException("modèles absents")),
            natif,
            () =>
            {
                fabriques++;
                return new ExtracteurFictif("ppocr-tiny", []);
            });

        selecteur.Extract("pdfpig", new MemoryStream());

        Assert.Equal(0, fabriques);
    }

    [Fact]
    public void Extract_MoteurInconnuLeveArgumentException()
    {
        var selecteur = CreerSelecteur(new ExtracteurFictif("pdfpig", []), new ExtracteurFictif("ppocr-tiny", []));

        Assert.Throws<ArgumentException>(() => selecteur.Extract("tesseract", new MemoryStream()));
    }

    private static ExtractionSelector CreerSelecteur(ExtracteurFictif natif, ExtracteurFictif ocr) =>
        new(new AutomaticExtraction(natif, () => ocr), natif, () => ocr);

    private sealed class ExtracteurFictif(string nom, IReadOnlyList<PageExtractionResult> pages) : ITextExtractionEngine
    {
        public int Appels { get; private set; }
        public string Name => nom;

        public IReadOnlyList<PageExtractionResult> Extract(Stream fluxDocument, CancellationToken cancellationToken = default)
        {
            Appels++;
            return pages;
        }
    }
}
