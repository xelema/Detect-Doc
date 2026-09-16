using System.IO.Compression;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using SkiaSharp;

namespace DetectDoc.Tests;

// Fixtures synthétiques réelles : chaque générateur produit un fichier décodable par la
// bibliothèque qui le lira, pas seulement sa signature.
internal static class FichiersDeTest
{
    public static byte[] Png() => EncoderImage(SKEncodedImageFormat.Png);

    public static byte[] Jpeg() => EncoderImage(SKEncodedImageFormat.Jpeg);

    public static byte[] WebP() => EncoderImage(SKEncodedImageFormat.Webp);

    // BMP 1x1 24 bpp non compressé : SkiaSharp ne sait pas l'encoder, l'en-tête est écrit à la main.
    public static byte[] Bmp() =>
    [
        0x42, 0x4D, 0x3A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x36, 0x00, 0x00, 0x00,
        0x28, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00,
        0x18, 0x00, 0x00, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0x13, 0x0B, 0x00, 0x00,
        0x13, 0x0B, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0xFF, 0xFF, 0xFF, 0x00
    ];

    // GIF89a 1x1 : SkiaSharp décode mais n'encode pas le GIF.
    public static byte[] Gif() => Convert.FromBase64String(
        "R0lGODlhAQABAAAAACH5BAEKAAEALAAAAAABAAEAAAICTAEAOw==");

    public static byte[] Docx(params string[] paragraphes)
    {
        using var flux = new MemoryStream();
        using (var document = WordprocessingDocument.Create(flux, WordprocessingDocumentType.Document))
        {
            var partie = document.AddMainDocumentPart();
            partie.Document = new Document(new Body(
                paragraphes.Select(texte => new Paragraph(new Run(new Text(texte))))));
            partie.Document.Save();
        }

        return flux.ToArray();
    }

    public static byte[] DocxAvecTableau(params string[] cellules)
    {
        using var flux = new MemoryStream();
        using (var document = WordprocessingDocument.Create(flux, WordprocessingDocumentType.Document))
        {
            var partie = document.AddMainDocumentPart();
            var ligne = new TableRow(cellules.Select(cellule =>
                new TableCell(new Paragraph(new Run(new Text(cellule))))));
            partie.Document = new Document(new Body(new Table(ligne)));
            partie.Document.Save();
        }

        return flux.ToArray();
    }

    // Un paragraphe portant un w:tab et un w:br, pour vérifier que les séparateurs survivent.
    public static byte[] DocxAvecTabEtSaut()
    {
        using var flux = new MemoryStream();
        using (var document = WordprocessingDocument.Create(flux, WordprocessingDocumentType.Document))
        {
            var partie = document.AddMainDocumentPart();
            partie.Document = new Document(new Body(new Paragraph(new Run(
                new Text("RIB"),
                new TabChar(),
                new Text("FR76"),
                new Break(),
                new Text("fin")))));
            partie.Document.Save();
        }

        return flux.ToArray();
    }

    // DOCX valide dont seul word/document.xml est remplacé par du XML cassé : le package reste
    // ouvrable mais la lecture du corps échoue.
    public static byte[] DocxCorrompu()
    {
        using var source = new ZipArchive(new MemoryStream(Docx("texte")), ZipArchiveMode.Read);
        using var flux = new MemoryStream();
        using (var cible = new ZipArchive(flux, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entree in source.Entries)
            {
                using var copie = new StreamWriter(cible.CreateEntry(entree.FullName).Open());
                if (entree.FullName == "word/document.xml")
                {
                    copie.Write("<w:document><non-ferme");
                    continue;
                }

                using var lecture = new StreamReader(entree.Open());
                copie.Write(lecture.ReadToEnd());
            }
        }

        return flux.ToArray();
    }

    // Un paragraphe portant un w:noBreakHyphen et un w:softHyphen : les coupures de mot
    // portent un caractère que l'extraction doit conserver.
    public static byte[] DocxAvecTraitsDUnion()
    {
        using var flux = new MemoryStream();
        using (var document = WordprocessingDocument.Create(flux, WordprocessingDocumentType.Document))
        {
            var partie = document.AddMainDocumentPart();
            partie.Document = new Document(new Body(new Paragraph(new Run(
                new Text("RIB"),
                new NoBreakHyphen(),
                new Text("FR76"),
                new SoftHyphen(),
                new Text("fin")))));
            partie.Document.Save();
        }

        return flux.ToArray();
    }

    // mc:AlternateContent au niveau corps : mc:Fallback répète en VML ce que mc:Choice porte
    // en DrawingML, le texte d'une zone de texte ne doit être extrait qu'une fois.
    public static byte[] DocxAvecContenuAlternatif()
    {
        using var flux = new MemoryStream();
        using (var document = WordprocessingDocument.Create(flux, WordprocessingDocumentType.Document))
        {
            var partie = document.AddMainDocumentPart();
            partie.Document = new Document(new Body(new AlternateContent(
                new AlternateContentChoice(new Paragraph(new Run(new Text("texte retenu"))))
                {
                    Requires = "wps"
                },
                new AlternateContentFallback(new Paragraph(new Run(new Text("texte duplique")))))));
            partie.Document.Save();
        }

        return flux.ToArray();
    }

    // Signature PK puis archive tronquée : le conteneur est reconnu mais illisible.
    public static byte[] PkTronque() => Docx("texte")[..20];

    // Conteneur zip sans word/document.xml : la signature PK seule ne fait pas un DOCX.
    public static byte[] ZipSansDocx()
    {
        using var flux = new MemoryStream();
        using (var archive = new ZipArchive(flux, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entree = new StreamWriter(archive.CreateEntry("xl/workbook.xml").Open());
            entree.Write("<x/>");
        }

        return flux.ToArray();
    }

    private static byte[] EncoderImage(SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(16, 8);
        bitmap.Erase(SKColors.White);
        using var image = SKImage.FromBitmap(bitmap);
        using var donnees = image.Encode(format, 90);
        return donnees.ToArray();
    }
}
