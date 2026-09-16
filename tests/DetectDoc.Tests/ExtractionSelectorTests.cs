using DetectDoc.Core.Engines;
using DetectDoc.Infrastructure.Engines;
using Xunit;

namespace DetectDoc.Tests;

public class ExtractionSelectorTests
{
    private static readonly byte[] Pdf = PdfDeTest.Construire("Texte natif");
    private static readonly byte[] Png = FichiersDeTest.Png();
    private static readonly byte[] Docx = FichiersDeTest.Docx("Texte word");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("auto")]
    [InlineData("AUTO")]
    public void Extract_AutoAppliqueLaPolitiqueDeRepliSurPdf(string? engine)
    {
        var natif = new ExtracteurFictif("pdfpig", [new(1, "Texte natif", null)]);
        var ocr = new ExtracteurFictif("ppocr-tiny", [new(1, "Texte OCR", 0.9f)]);
        var selecteur = CreerSelecteur(natif, ocr);

        var resultat = selecteur.Extract(engine, new MemoryStream(Pdf));

        Assert.Equal("pdfpig", resultat.Moteur);
        Assert.Equal(1, natif.Appels);
        Assert.Equal(0, ocr.Appels);
    }

    [Fact]
    public void Extract_AutoBasculeSurLOcrQuandUnePagePdfEstVide()
    {
        var natif = new ExtracteurFictif("pdfpig", [new(1, "", null)]);
        var ocr = new ExtracteurFictif("ppocr-tiny", [new(1, "Texte OCR", 0.9f)]);
        var selecteur = CreerSelecteur(natif, ocr);

        var resultat = selecteur.Extract("auto", new MemoryStream(Pdf));

        Assert.Equal("ppocr-tiny", resultat.Moteur);
        Assert.Equal(1, ocr.Appels);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("auto")]
    [InlineData("ppocr-tiny")]
    public void Extract_ImageVaALOcrSansPasserParLeNatif(string? engine)
    {
        var natif = new ExtracteurFictif("pdfpig", [new(1, "Texte natif", null)]);
        var ocr = new ExtracteurFictif("ppocr-tiny", [new(1, "Texte OCR", 0.9f)]);
        var selecteur = CreerSelecteur(natif, ocr);

        var resultat = selecteur.Extract(engine, new MemoryStream(Png));

        Assert.Equal("ppocr-tiny", resultat.Moteur);
        Assert.Equal(0, natif.Appels);
        Assert.Equal(1, ocr.Appels);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("auto")]
    [InlineData("openxml")]
    public void Extract_DocxVaAuMoteurOpenXml(string? engine)
    {
        var docx = new ExtracteurFictif("openxml", [new(1, "Texte word", null)]);
        var selecteur = CreerSelecteur(
            new ExtracteurFictif("pdfpig", []),
            new ExtracteurFictif("ppocr-tiny", []),
            docx);

        var resultat = selecteur.Extract(engine, new MemoryStream(Docx));

        Assert.Equal("openxml", resultat.Moteur);
        Assert.Equal(1, docx.Appels);
    }

    [Theory]
    [InlineData("pdfpig", "png")]
    [InlineData("openxml", "png")]
    [InlineData("ppocr-tiny", "docx")]
    [InlineData("pdfpig", "docx")]
    [InlineData("openxml", "pdf")]
    public void Extract_CombinaisonMoteurFormatIncompatibleLeveArgumentException(string engine, string fixture)
    {
        var contenu = fixture switch
        {
            "png" => Png,
            "docx" => Docx,
            _ => Pdf
        };
        var selecteur = CreerSelecteur(new ExtracteurFictif("pdfpig", []), new ExtracteurFictif("ppocr-tiny", []));

        var exception = Assert.Throws<ArgumentException>(
            () => selecteur.Extract(engine, new MemoryStream(contenu)));
        Assert.Equal("engine", exception.ParamName);
    }

    [Fact]
    public void Extract_PdfPigUtiliseLeMoteurNommeSansPasserParAuto()
    {
        var natifDirect = new ExtracteurFictif("pdfpig", [new(1, "Natif direct", null)]);
        var natifAuto = new ExtracteurFictif("pdfpig", [new(1, "", null)]);
        var ocr = new ExtracteurFictif("ppocr-tiny", [new(1, "Texte OCR", 0.9f)]);
        var selecteur = new ExtractionSelector(
            new AutomaticExtraction(natifAuto, () => ocr),
            natifDirect,
            () => ocr,
            new ExtracteurFictif("openxml", []));

        var resultat = selecteur.Extract("PDFPIG", new MemoryStream(Pdf));

        Assert.Equal("Natif direct", resultat.Pages[0].Texte);
        Assert.Equal(0, natifAuto.Appels);
        Assert.Equal(0, ocr.Appels);
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
            },
            new ExtracteurFictif("openxml", []));

        selecteur.Extract("pdfpig", new MemoryStream(Pdf));

        Assert.Equal(0, fabriques);
    }

    [Fact]
    public void Extract_MoteurInconnuLeveArgumentException()
    {
        var selecteur = CreerSelecteur(new ExtracteurFictif("pdfpig", []), new ExtracteurFictif("ppocr-tiny", []));

        var exception = Assert.Throws<ArgumentException>(
            () => selecteur.Extract("tesseract", new MemoryStream(Pdf)));
        Assert.Equal("engine", exception.ParamName);
    }

    [Fact]
    public void Extract_MoteurInconnuEstRefuseMemeSurUnFormatInconnu()
    {
        var selecteur = CreerSelecteur(new ExtracteurFictif("pdfpig", []), new ExtracteurFictif("ppocr-tiny", []));

        var exception = Assert.Throws<ArgumentException>(
            () => selecteur.Extract("tesseract", new MemoryStream("pas un document"u8.ToArray())));
        Assert.Equal("engine", exception.ParamName);
    }

    [Fact]
    public void Extract_FormatInconnuLeveUnsupportedDocumentFormat()
    {
        var selecteur = CreerSelecteur(new ExtracteurFictif("pdfpig", []), new ExtracteurFictif("ppocr-tiny", []));

        Assert.Throws<UnsupportedDocumentFormatException>(
            () => selecteur.Extract("auto", new MemoryStream("pas un document"u8.ToArray())));
    }

    private static ExtractionSelector CreerSelecteur(
        ExtracteurFictif natif,
        ExtracteurFictif ocr,
        ExtracteurFictif? docx = null) =>
        new(new AutomaticExtraction(natif, () => ocr), natif, () => ocr, docx ?? new ExtracteurFictif("openxml", []));

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
