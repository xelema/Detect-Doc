namespace DetectDoc.Core.Classification;

public sealed record ClassificationResult(string Type, double Confiance, string? ExecutionId = null);

public sealed record TypeDocumentaire(string Id, string? Libelle, string? Description);

public interface IDocumentClassifier
{
    Task<ClassificationResult> ClassifyAsync(
        string texte,
        IReadOnlyCollection<TypeDocumentaire> types,
        CancellationToken cancellationToken = default);
}
