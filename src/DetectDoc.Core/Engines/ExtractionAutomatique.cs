namespace DetectDoc.Core.Engines;

public sealed class AutomaticExtraction(
    ITextExtractionEngine extracteurNatif,
    Func<ITextExtractionEngine> extracteurOcr)
{
    public AutomaticExtractionResult Extract(Stream fluxDocument, CancellationToken cancellationToken = default)
    {
        var pages = extracteurNatif.Extract(fluxDocument, cancellationToken);
        if (pages.All(page => !string.IsNullOrWhiteSpace(page.Texte)))
        {
            return new AutomaticExtractionResult(pages, extracteurNatif.Name);
        }

        // Le moteur OCR passe par une fabrique : il charge ses modèles ONNX à la construction,
        // et un document dont le texte natif suffit ne doit pas dépendre de leur présence.
        var ocr = extracteurOcr();
        return new AutomaticExtractionResult(ocr.Extract(fluxDocument, cancellationToken), ocr.Name);
    }
}

public sealed record AutomaticExtractionResult(
    IReadOnlyList<PageExtractionResult> Pages,
    string Moteur);
