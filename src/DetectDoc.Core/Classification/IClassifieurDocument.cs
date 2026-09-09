namespace DetectDoc.Core.Classification;

public sealed record ClassificationResult(string Type, double Confiance, string? ExecutionId = null);

public interface IDocumentClassifier
{
    Task<ClassificationResult> ClassifyAsync(
        string texte,
        IReadOnlyCollection<string> types,
        CancellationToken cancellationToken = default);
}
