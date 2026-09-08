namespace DetectDoc.Core.Classification;

public sealed record ResultatClassification(string Type, double Confiance);

public interface IClassifieurDocument
{
    Task<ResultatClassification> ClasserAsync(
        string texte,
        IReadOnlyCollection<string> types,
        CancellationToken cancellationToken = default);
}
