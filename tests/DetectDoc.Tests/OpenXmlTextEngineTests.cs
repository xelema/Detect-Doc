using System.IO.Compression;
using DetectDoc.Core.Engines;
using DetectDoc.Infrastructure.Engines;
using Xunit;

namespace DetectDoc.Tests;

public class OpenXmlTextEngineTests
{
    private readonly OpenXmlTextEngine _moteur = new();

    [Fact]
    public void Extract_JointLesParagraphesDansLOrdre()
    {
        using var flux = new MemoryStream(FichiersDeTest.Docx("Premier.", "Second.", "Troisième."));

        var pages = _moteur.Extract(flux);

        var page = Assert.Single(pages);
        Assert.Equal(1, page.NumeroPage);
        Assert.Equal("Premier." + Environment.NewLine + "Second." + Environment.NewLine + "Troisième.", page.Texte);
        Assert.Null(page.Confiance);
    }

    [Fact]
    public void Extract_ConserveTabulationsEtSautsDeLigne()
    {
        using var flux = new MemoryStream(FichiersDeTest.DocxAvecTabEtSaut());

        var texte = _moteur.Extract(flux)[0].Texte;

        Assert.Equal("RIB\tFR76\nfin", texte);
    }

    [Fact]
    public void Extract_InclutLeTexteDesTableaux()
    {
        var docx = FichiersDeTest.DocxAvecTableau("Cellule A", "Cellule B");
        using var flux = new MemoryStream(docx);

        var texte = _moteur.Extract(flux)[0].Texte;

        Assert.Contains("Cellule A", texte);
        Assert.Contains("Cellule B", texte);
    }

    [Fact]
    public void Extract_DocxSansTexteRetourneUnePageVide()
    {
        using var flux = new MemoryStream(FichiersDeTest.Docx());

        var page = Assert.Single(_moteur.Extract(flux));
        Assert.Equal(string.Empty, page.Texte);
    }

    [Fact]
    public void Extract_RefuseUnAutreFormat()
    {
        using var flux = new MemoryStream(PdfDeTest.Construire("texte"));

        var exception = Assert.Throws<ArgumentException>(() => _moteur.Extract(flux));
        Assert.Equal("engine", exception.ParamName);
    }

    [Fact]
    public void Extract_SignaleUnDocxCorrompu()
    {
        // Package complet mais document.xml tronqué : la signature passe, l'échec de lecture
        // doit ressortir en erreur de document, pas en faute.
        using var flux = new MemoryStream(FichiersDeTest.DocxCorrompu());

        Assert.Throws<InvalidDataException>(() => _moteur.Extract(flux));
    }

    [Fact]
    public void Extract_SignaleUneErreurDeLectureCommeDocumentInvalide()
    {
        // Le flux tombe en panne passé l'en-tête : l'archive s'ouvre sur la fin du flux,
        // l'échec IO doit ressortir en document illisible (422), pas en faute interne.
        using var flux = new FluxEnEchec(FichiersDeTest.Docx("texte"));

        Assert.Throws<InvalidDataException>(() => _moteur.Extract(flux));
    }

    private sealed class FluxEnEchec(byte[] contenu) : MemoryStream(contenu)
    {
        public override int Read(byte[] buffer, int offset, int count) =>
            Position > 500 ? throw new IOException("Panne simulée.") : base.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer) =>
            Position > 500 ? throw new IOException("Panne simulée.") : base.Read(buffer);
    }
}
