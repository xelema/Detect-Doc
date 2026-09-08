namespace DetectDoc.Core.Engines;

public sealed class ExtractionAutomatique(
    ITextExtractionEngine extracteurNatif,
    ITextExtractionEngine extracteurOcr)
{
    public ResultatExtractionAutomatique Extract(Stream fluxDocument, CancellationToken cancellationToken = default)
    {
        var pages = extracteurNatif.Extract(fluxDocument, cancellationToken);
        return pages.All(page => !string.IsNullOrWhiteSpace(page.Texte))
            ? new ResultatExtractionAutomatique(pages, extracteurNatif.Name)
            : new ResultatExtractionAutomatique(
                extracteurOcr.Extract(fluxDocument, cancellationToken),
                extracteurOcr.Name);
    }
}

public sealed record ResultatExtractionAutomatique(
    IReadOnlyList<ResultatExtractionPage> Pages,
    string Moteur);
