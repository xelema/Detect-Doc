using DetectDoc.Core.Engines;

namespace DetectDoc.Infrastructure.Engines;

// Propriétaire du vocabulaire des moteurs côté requête. La fabrique OCR garde la construction
// paresseuse : le constructeur de PpOcrEngine charge les modèles ONNX.
public sealed class ExtractionSelector(
    AutomaticExtraction automatique,
    ITextExtractionEngine pdfPig,
    Func<ITextExtractionEngine> ppOcr) : IExtractionSelector
{
    public ExtractionResult Extract(
        string? engine,
        Stream document,
        CancellationToken cancellationToken = default) => engine?.ToLowerInvariant() switch
        {
            null or "" or "auto" => automatique.Extract(document, cancellationToken),
            "pdfpig" => Extrait(pdfPig, document, cancellationToken),
            "ppocr-tiny" => Extrait(ppOcr(), document, cancellationToken),
            _ => throw new ArgumentException("Le moteur doit être auto, pdfpig ou ppocr-tiny.", nameof(engine))
        };

    private static ExtractionResult Extrait(
        ITextExtractionEngine moteur,
        Stream document,
        CancellationToken cancellationToken) =>
        new(moteur.Extract(document, cancellationToken), moteur.Name);
}
