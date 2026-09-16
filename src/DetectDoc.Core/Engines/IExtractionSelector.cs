namespace DetectDoc.Core.Engines;

public sealed record ExtractionResult(
    IReadOnlyList<PageExtractionResult> Pages,
    string Moteur);

// Résout le jeton `engine` d'une requête vers le moteur effectif et exécute l'extraction.
// `auto` dispatche selon le format détecté : repli natif puis OCR d'AutomaticExtraction sur
// PDF, OCR direct sur image, OpenXml sur DOCX.
public interface IExtractionSelector
{
    ExtractionResult Extract(
        string? engine,
        Stream document,
        CancellationToken cancellationToken = default);
}
