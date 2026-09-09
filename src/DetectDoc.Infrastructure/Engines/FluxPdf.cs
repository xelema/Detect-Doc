namespace DetectDoc.Infrastructure.Engines;

internal static class PdfStream
{
    // Les moteurs relisent le flux à plusieurs reprises et le parseur accepte des octets
    // arbitraires : on exige un flux rejouable et une signature PDF avant tout traitement.
    public static void Validate(Stream fluxDocument)
    {
        ArgumentNullException.ThrowIfNull(fluxDocument);
        if (!fluxDocument.CanRead || !fluxDocument.CanSeek)
        {
            throw new ArgumentException("Le flux doit être lisible et seekable.", nameof(fluxDocument));
        }

        Span<byte> signature = stackalloc byte[5];
        fluxDocument.Position = 0;
        var estPdf = fluxDocument.ReadAtLeast(signature, signature.Length, throwOnEndOfStream: false) == signature.Length
            && signature.SequenceEqual("%PDF-"u8);
        fluxDocument.Position = 0;

        if (!estPdf)
        {
            throw new InvalidDataException("Le flux ne porte pas une signature PDF valide.");
        }
    }
}
