using DetectDoc.Core.Engines;
using Xunit;

namespace DetectDoc.Tests;

public class DocumentFormatsTests
{
    public static IEnumerable<object[]> Images() =>
    [
        ["png", FichiersDeTest.Png()],
        ["jpeg", FichiersDeTest.Jpeg()],
        ["bmp", FichiersDeTest.Bmp()],
        ["webp", FichiersDeTest.WebP()],
        ["gif", FichiersDeTest.Gif()]
    ];

    [Fact]
    public void Detect_ReconnaitUnPdf()
    {
        using var flux = new MemoryStream(PdfDeTest.Construire("texte"));

        Assert.Equal(DocumentFileFormat.Pdf, DocumentFormats.Detect(flux));
        Assert.Equal(0, flux.Position);
    }

    [Theory]
    [MemberData(nameof(Images))]
    public void Detect_ReconnaitLesImages(string format, byte[] contenu)
    {
        using var flux = new MemoryStream(contenu);

        Assert.True(DocumentFileFormat.Image == DocumentFormats.Detect(flux), format);
    }

    [Fact]
    public void Detect_ReconnaitUnDocx()
    {
        using var flux = new MemoryStream(FichiersDeTest.Docx("Relevé bancaire"));

        Assert.Equal(DocumentFileFormat.Docx, DocumentFormats.Detect(flux));
        Assert.Equal(0, flux.Position);
    }

    [Fact]
    public void Detect_SignaleUneArchivePkIlisible()
    {
        // Signature PK mais archive tronquée : conteneur reconnu, contenu illisible (422).
        using var flux = new MemoryStream(FichiersDeTest.PkTronque());

        Assert.Throws<InvalidDataException>(() => DocumentFormats.Detect(flux));
    }

    [Fact]
    public void Detect_RejetteUnZipSansPartieDocument()
    {
        using var flux = new MemoryStream(FichiersDeTest.ZipSansDocx());

        Assert.Throws<UnsupportedDocumentFormatException>(() => DocumentFormats.Detect(flux));
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B })]
    [InlineData(new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 })]
    public void Detect_RejetteLesSignaturesInconnues(byte[] contenu)
    {
        using var flux = new MemoryStream(contenu);

        Assert.Throws<UnsupportedDocumentFormatException>(() => DocumentFormats.Detect(flux));
    }

    [Fact]
    public void Detect_ExigeUnFluxSeekable()
    {
        using var flux = new NonSeekableStream(FichiersDeTest.Png());

        Assert.Throws<ArgumentException>(() => DocumentFormats.Detect(flux));
    }

    private sealed class NonSeekableStream(byte[] contenu) : MemoryStream(contenu)
    {
        public override bool CanSeek => false;
    }
}
