namespace DetectDoc.Core.Engines;

public sealed record ExtractionResult(
    IReadOnlyList<PageExtractionResult> Pages,
    string Moteur);

// Résout le jeton `engine` d'une requête vers le moteur effectif et exécute l'extraction.
// `auto` applique la politique de repli natif puis OCR portée par AutomaticExtraction.
public interface IExtractionSelector
{
    ExtractionResult Extract(
        string? engine,
        Stream document,
        CancellationToken cancellationToken = default);
}
