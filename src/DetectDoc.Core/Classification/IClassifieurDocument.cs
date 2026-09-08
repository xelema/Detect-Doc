namespace DetectDoc.Core.Classification;

public sealed record ResultatClassification(string Type, double Confiance, string? ExecutionId = null);

public interface IClassifieurDocument
{
    Task<ResultatClassification> ClasserAsync(
        string texte,
        IReadOnlyCollection<string> types,
        CancellationToken cancellationToken = default);
}
