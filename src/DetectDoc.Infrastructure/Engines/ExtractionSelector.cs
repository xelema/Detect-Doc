using DetectDoc.Core.Engines;

namespace DetectDoc.Infrastructure.Engines;

// Propriétaire du vocabulaire des moteurs côté requête et du dispatch par format. La fabrique
// OCR garde la construction paresseuse : le constructeur de PpOcrEngine charge les modèles ONNX.
public sealed class ExtractionSelector(
    AutomaticExtraction automatique,
    ITextExtractionEngine pdfPig,
    Func<ITextExtractionEngine> ppOcr,
    ITextExtractionEngine openXml) : IExtractionSelector
{
    public ExtractionResult Extract(
        string? engine,
        Stream document,
        CancellationToken cancellationToken = default)
    {
        var format = DocumentFormats.Detect(document);
        var jeton = engine?.ToLowerInvariant();
        return (jeton, format) switch
        {
            (null or "" or "auto", DocumentFileFormat.Pdf) =>
                automatique.Extract(document, cancellationToken),
            (null or "" or "auto", DocumentFileFormat.Image) =>
                Extrait(ppOcr(), document, cancellationToken),
            (null or "" or "auto", DocumentFileFormat.Docx) =>
                Extrait(openXml, document, cancellationToken),
            ("pdfpig", DocumentFileFormat.Pdf) =>
                Extrait(pdfPig, document, cancellationToken),
            ("ppocr-tiny", DocumentFileFormat.Pdf or DocumentFileFormat.Image) =>
                Extrait(ppOcr(), document, cancellationToken),
            ("openxml", DocumentFileFormat.Docx) =>
                Extrait(openXml, document, cancellationToken),
            ("pdfpig" or "ppocr-tiny" or "openxml", _) => throw new ArgumentException(
                $"Le moteur {jeton} ne prend pas en charge ce format de document.", nameof(engine)),
            _ => throw new ArgumentException(
                "Le moteur doit être auto, pdfpig, ppocr-tiny ou openxml.", nameof(engine))
        };
    }

    private static ExtractionResult Extrait(
        ITextExtractionEngine moteur,
        Stream document,
        CancellationToken cancellationToken) =>
        new(moteur.Extract(document, cancellationToken), moteur.Name);
}
