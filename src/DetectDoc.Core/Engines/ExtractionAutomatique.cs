namespace DetectDoc.Core.Engines;

public sealed class AutomaticExtraction(
    ITextExtractionEngine extracteurNatif,
    Func<ITextExtractionEngine> extracteurOcr)
{
    public ExtractionResult Extract(Stream fluxDocument, CancellationToken cancellationToken = default)
    {
        var pages = extracteurNatif.Extract(fluxDocument, cancellationToken);
        if (pages.All(page => !string.IsNullOrWhiteSpace(page.Texte)))
        {
            return new ExtractionResult(pages, extracteurNatif.Name);
        }

        // Le moteur OCR passe par une fabrique : il charge ses modèles ONNX à la construction,
        // et un document dont le texte natif suffit ne doit pas dépendre de leur présence.
        var ocr = extracteurOcr();
        return new ExtractionResult(ocr.Extract(fluxDocument, cancellationToken), ocr.Name);
    }
}
