using System.IO.Compression;

namespace DetectDoc.Core.Engines;

// La signature du contenu fait foi, jamais l'extension ni le Content-Type annoncés.
// DOCX est une archive zip parmi d'autres : seule la présence de word/document.xml le distingue.
public static class DocumentFormats
{
    public static DocumentFileFormat Detect(Stream fluxDocument)
    {
        ArgumentNullException.ThrowIfNull(fluxDocument);
        if (!fluxDocument.CanRead || !fluxDocument.CanSeek)
        {
            throw new ArgumentException("Le flux doit être lisible et seekable.", nameof(fluxDocument));
        }

        Span<byte> enTete = stackalloc byte[12];
        fluxDocument.Position = 0;
        var lu = fluxDocument.ReadAtLeast(enTete, enTete.Length, throwOnEndOfStream: false);
        fluxDocument.Position = 0;
        var tete = enTete[..lu];

        if (tete.StartsWith("%PDF-"u8))
        {
            return DocumentFileFormat.Pdf;
        }

        if (EstImage(tete))
        {
            return DocumentFileFormat.Image;
        }

        if (tete.StartsWith("PK"u8))
        {
            return EstDocx(fluxDocument)
                ? DocumentFileFormat.Docx
                : throw new UnsupportedDocumentFormatException();
        }

        throw new UnsupportedDocumentFormatException();
    }

    // Signatures non ASCII en octets explicites : un littéral u8 encoderait \x89 et \xFF en UTF-8.
    private static readonly byte[] SignaturePng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] SignatureJpeg = [0xFF, 0xD8, 0xFF];

    private static bool EstImage(ReadOnlySpan<byte> tete) =>
        tete.StartsWith(SignaturePng)
        || tete.StartsWith(SignatureJpeg)
        || tete.StartsWith("BM"u8)
        || tete.StartsWith("GIF8"u8)
        || tete.StartsWith("RIFF"u8) && tete.Length >= 12 && tete[8..].StartsWith("WEBP"u8);

    private static bool EstDocx(Stream fluxDocument)
    {
        try
        {
            using var archive = new ZipArchive(fluxDocument, ZipArchiveMode.Read, leaveOpen: true);
            return archive.GetEntry("word/document.xml") is not null;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            // Signature PK mais archive illisible : conteneur reconnu corrompu, pas un format inconnu.
            throw new InvalidDataException("L'archive du document est illisible.", exception);
        }
        finally
        {
            fluxDocument.Position = 0;
        }
    }
}
