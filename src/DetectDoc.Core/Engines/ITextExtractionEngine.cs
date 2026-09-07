namespace DetectDoc.Core.Engines;

public readonly record struct ResultatExtractionPage(
    int NumeroPage,
    string Texte,
    float? Confiance);

public interface ITextExtractionEngine : IDisposable
{
    string Name { get; }
    string Version { get; }
    IReadOnlyDictionary<string, object> Parametres { get; }
    IReadOnlyList<ResultatExtractionPage> Extract(
        Stream fluxDocument,
        CancellationToken cancellationToken = default);
}
