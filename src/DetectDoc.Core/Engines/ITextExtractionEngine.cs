namespace DetectDoc.Core.Engines;

public readonly record struct PageExtractionResult(
    int NumeroPage,
    string Texte,
    float? Confiance);

public interface ITextExtractionEngine
{
    string Name { get; }

    IReadOnlyList<PageExtractionResult> Extract(
        Stream fluxDocument,
        CancellationToken cancellationToken = default);
}
